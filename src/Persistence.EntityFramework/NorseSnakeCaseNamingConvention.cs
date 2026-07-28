using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Model-finalizing convention that renames every relational EF metadata object to snake_case: table
/// names, primary key names, column names, default-constraint names, key names, foreign key constraint
/// names, and index names. JSON-mapped entities are skipped except for the root entity's own container
/// column name, which is the only relational identity a JSON structure actually owns — a nested entity
/// (owned by an already-JSON-mapped parent) shares that same container, so renaming it too corrupts the
/// shaper EF Core 11 preview6 compiles for the query (see the exclusion's own remarks below). Confirmed
/// against upstream: <see href="https://github.com/dotnet/efcore/issues/37417"/> is the identical crash
/// (same stack trace, down to the exact <c>CreateJsonShapers</c> frame) against <c>dotnet/efcore</c>
/// itself, closed as a duplicate of
/// <see href="https://github.com/efcore/EFCore.NamingConventions/issues/346"/>, whose merged fix
/// (<see href="https://github.com/efcore/EFCore.NamingConventions/pull/347"/>) introduced the exact
/// root-vs-nested distinction ported here — this platform doesn't consume that package (this in-house
/// convention replaced it 2026-07-22), so the fix has to live here too. <see cref="HistoryRow"/> — the
/// synthetic entity
/// <c>HistoryRepository.EnsureModel()</c> builds to generate the migrations-history table's DDL — is
/// skipped entirely: <c>HistoryRepository.TableName</c> (used verbatim for raw SQL such as Npgsql's
/// <c>LOCK TABLE</c> during <c>AcquireDatabaseLockAsync</c>) is sourced from
/// <c>RelationalOptionsExtension.MigrationsHistoryTableName</c>, never from this convention pipeline —
/// renaming <see cref="HistoryRow"/>'s table here would desync the DDL-generated table name from the
/// name EF's own internals use to query/lock it, a live 42P01 "relation does not exist" bug this
/// exclusion exists to prevent.
/// </summary>
/// <param name="applyProviderSpecificRenames">
/// Optional provider-specific extension point, invoked once per entity after this convention's own
/// renames. This convention has no idea what it does — it only hands the entity and its own
/// <see cref="SnakeCaseNameRewriter.RewriteName"/> function to whatever the registering provider
/// supplied via <see cref="NorseDbContextOptionsExtensions.ApplyNorseConventions"/>, or nothing at all.
/// SQL Server temporal history table renaming is supplied this way from
/// <c>Norse.Persistence.EntityFramework.SqlServer</c> — see that project's
/// <c>NorseSqlServerContextExtensions</c> — because <c>IsTemporal()</c>/<c>GetHistoryTableName()</c> are
/// SQL-Server-only EF APIs this provider-neutral project must never reference directly.
/// </param>
sealed class NorseSnakeCaseNamingConvention(Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames) :
	IModelFinalizingConvention
{
	public void ProcessModelFinalizing(
		IConventionModelBuilder builder, IConventionContext<IConventionModelBuilder> context)
	{
		foreach (var entity in builder.Metadata.GetEntityTypes())
		{
			if (entity.ClrType == typeof(HistoryRow))
				continue;

			if (entity.IsMappedToJson())
			{
				// Renaming a NESTED JSON entity's container column (one whose owner is itself
				// JSON-mapped) is what triggers EF Core 11 preview6's JSON shaper:
				// RelationalShapedQueryCompilingExpressionVisitor.ShaperProcessingExpressionVisitor
				// .CreateJsonShapers throws ArgumentNullException ("Value cannot be null. (Parameter
				// 'key')") compiling the shaper for any query that materializes the owning entity,
				// regardless of the JSON payload's actual contents (even a NULL container reproduces
				// it) -- a pure model-shape defect, not a data-dependent one. Reproduces identically on
				// SQLite, so it is not Npgsql-specific. Only the ROOT JSON entity actually owns a
				// container column; nested entities share it, so skipping them here is correct, not
				// just a crash workaround.
				var owningEntity = entity.FindOwnership()?.PrincipalEntityType;
				if (owningEntity is not null && owningEntity.IsMappedToJson())
					continue;

				var containerColumnName = entity.GetContainerColumnName();
				if (!string.IsNullOrWhiteSpace(containerColumnName))
					entity.SetContainerColumnName(SnakeCaseNameRewriter.RewriteName(containerColumnName, uppercase: false));
				continue;
			}

			var tableName = entity.GetTableName();
			if (string.IsNullOrWhiteSpace(tableName))
				continue;

			entity.SetTableName(SnakeCaseNameRewriter.RewriteName(tableName, uppercase: false));

			var primaryKey = entity.FindPrimaryKey();
			if (primaryKey is not null)
			{
				var primaryKeyName = primaryKey.GetName();
				if (!string.IsNullOrWhiteSpace(primaryKeyName))
					primaryKey.SetName(SnakeCaseNameRewriter.RewriteName(primaryKeyName, uppercase: false));
			}

			foreach (var property in entity.GetProperties())
			{
				property.SetColumnName(SnakeCaseNameRewriter.RewriteName(property.GetColumnName(), uppercase: false));

				var defaultConstraintName = property.GetDefaultConstraintName();
				if (!string.IsNullOrWhiteSpace(defaultConstraintName))
					property.SetDefaultConstraintName(SnakeCaseNameRewriter.RewriteName(defaultConstraintName, uppercase: false));
			}

			foreach (var complexProperty in entity.GetComplexProperties())
				RenameComplexType(complexProperty.ComplexType);

			foreach (var key in entity.GetKeys())
			{
				var keyName = key.GetName();
				if (!string.IsNullOrWhiteSpace(keyName))
					key.SetName(SnakeCaseNameRewriter.RewriteName(keyName, uppercase: false));
			}

			foreach (var foreignKey in entity.GetForeignKeys())
			{
				var constraintName = foreignKey.GetConstraintName();
				if (!string.IsNullOrWhiteSpace(constraintName))
					foreignKey.SetConstraintName(SnakeCaseNameRewriter.RewriteName(constraintName, uppercase: false));
			}

			foreach (var index in entity.GetIndexes())
			{
				var databaseName = index.GetDatabaseName();
				if (!string.IsNullOrWhiteSpace(databaseName))
					index.SetDatabaseName(SnakeCaseNameRewriter.RewriteName(databaseName, uppercase: false));
			}

			applyProviderSpecificRenames?.Invoke(entity, name => SnakeCaseNameRewriter.RewriteName(name, uppercase: false));
		}
	}

	/// <summary>
	/// Complex types (<c>ComplexProperty&lt;T&gt;()</c>) never appear in <c>GetEntityTypes()</c> -- they
	/// hang off their declaring entity's <c>GetComplexProperties()</c>, so the main loop above never sees
	/// them. A JSON-mapped complex property (<c>.ToJson()</c>) owns its own
	/// container column exactly like a root JSON entity does, so it gets the same treatment. A non-JSON
	/// complex property maps its scalar properties onto ordinary columns on the owning table instead.
	/// Recursion stops the moment a JSON container is found: a complex property nested inside one shares
	/// that same physical column rather than owning one of its own, so renaming it would hit the identical
	/// shaper corruption the root-vs-nested JSON entity exclusion above exists to prevent.
	/// </summary>
	static void RenameComplexType(IConventionComplexType complexType)
	{
		var containerColumnName = complexType.GetContainerColumnName();
		if (!string.IsNullOrWhiteSpace(containerColumnName))
		{
			complexType.SetContainerColumnName(SnakeCaseNameRewriter.RewriteName(containerColumnName, uppercase: false));
			return;
		}

		foreach (var property in complexType.GetProperties())
			property.SetColumnName(SnakeCaseNameRewriter.RewriteName(property.GetColumnName(), uppercase: false));

		foreach (var nestedComplexProperty in complexType.GetComplexProperties())
			RenameComplexType(nestedComplexProperty.ComplexType);
	}
}
