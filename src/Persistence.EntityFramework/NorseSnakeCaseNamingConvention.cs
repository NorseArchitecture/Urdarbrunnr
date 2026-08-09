using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Norse.Persistence.EntityFramework;

/// <summary>
///     Model-finalizing convention that renames every relational EF metadata object to snake_case: table
///     names (including entity-splitting fragment tables, which are re-created under the rewritten
///     <see cref="StoreObjectIdentifier" /> with their property overrides migrated), primary key names,
///     column names, default-constraint names, key names, foreign key constraint names, and index names.
///     The one deliberate exception: fragment-bearing entities keep EF's per-table default key names —
///     an explicit key name is a single global annotation, so rewriting it would stamp the same
///     constraint name onto every fragment table and 42P07 on Postgres at migrate time (see the inline
///     remarks at the key-renaming site). JSON-mapped entities are skipped except for the root entity's own container
///     column name, which is the only relational identity a JSON structure actually owns — a nested entity
///     (owned by an already-JSON-mapped parent) shares that same container, so renaming it too corrupts the
///     shaper EF Core 11 preview6 compiles for the query (see the exclusion's own remarks below). Confirmed
///     against upstream: <see href="https://github.com/dotnet/efcore/issues/37417" /> is the identical crash
///     (same stack trace, down to the exact <c>CreateJsonShapers</c> frame) against <c>dotnet/efcore</c>
///     itself, closed as a duplicate of
///     <see href="https://github.com/efcore/EFCore.NamingConventions/issues/346" />, whose merged fix
///     (<see href="https://github.com/efcore/EFCore.NamingConventions/pull/347" />) introduced the exact
///     root-vs-nested distinction ported here — this platform doesn't consume that package (this in-house
///     convention replaced it 2026-07-22), so the fix has to live here too. <see cref="HistoryRow" /> — the
///     synthetic entity
///     <c>HistoryRepository.EnsureModel()</c> builds to generate the migrations-history table's DDL — is
///     skipped entirely: <c>HistoryRepository.TableName</c> (used verbatim for raw SQL such as Npgsql's
///     <c>LOCK TABLE</c> during <c>AcquireDatabaseLockAsync</c>) is sourced from
///     <c>RelationalOptionsExtension.MigrationsHistoryTableName</c>, never from this convention pipeline —
///     renaming <see cref="HistoryRow" />'s table here would desync the DDL-generated table name from the
///     name EF's own internals use to query/lock it, a live 42P01 "relation does not exist" bug this
///     exclusion exists to prevent.
/// </summary>
/// <param name="rewriteName">
///     The caller-supplied identifier rewrite delegate — one of <see cref="NorseNameRewriters" />'s
///     engine-native styles, or any other <see cref="Func{T, TResult}" /> the registering provider binding
///     chooses. This convention has no naming opinion of its own; it applies whatever function it's given.
/// </param>
/// <param name="applyProviderSpecificRenames">
///     Optional provider-specific extension point, invoked once per entity after this convention's own
///     renames. This convention has no idea what it does — it only hands the entity and the caller-supplied
///     <paramref name="rewriteName" /> delegate to whatever the registering provider supplied via
///     <see cref="NorseDbContextOptionsExtensions.ApplyNorseConventions" />, or nothing at all.
///     SQL Server temporal history table renaming is supplied this way from
///     <c>Norse.Persistence.EntityFramework.SqlServer</c>'s provider binding — see
///     <c>NorseSqlServerEfProvider.EntityRenameHook</c> — because
///     <c>IsTemporal()</c>/<c>GetHistoryTableName()</c> are SQL-Server-only EF APIs this provider-neutral
///     project must never reference directly.
/// </param>
sealed class NorseSnakeCaseNamingConvention(
	Func<string, string> rewriteName,
	Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames) :
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
					entity.SetContainerColumnName(rewriteName(containerColumnName));
				continue;
			}

			var tableName = entity.GetTableName();
			if (string.IsNullOrWhiteSpace(tableName))
				continue;

			entity.SetTableName(rewriteName(tableName));

			// Entity-splitting fragments (SplitToTable) are keyed by StoreObjectIdentifier, not by the
			// table-name annotation the SetTableName call above rewrites, so each one is renamed by
			// re-creation: a new fragment under the rewritten identifier, per-store property overrides
			// (which are what record fragment column membership) migrated across, then the stale
			// fragment removed. Skipping the migration step would silently fold the split columns back
			// into the main table.
			var hasMappingFragments = false;
			foreach (var fragment in entity.GetMappingFragments(StoreObjectType.Table).ToList())
			{
				hasMappingFragments = true;
				var storeObject = fragment.StoreObject;
				var rewrittenName = rewriteName(storeObject.Name);
				if (rewrittenName == storeObject.Name)
					continue;

				var renamed = StoreObjectIdentifier.Table(rewrittenName, storeObject.Schema);
				var renamedFragment = entity.GetOrCreateMappingFragment(renamed);
				if (fragment.IsTableExcludedFromMigrations is not null)
					renamedFragment.SetIsTableExcludedFromMigrations(fragment.IsTableExcludedFromMigrations);

				foreach (var property in entity.GetProperties())
				{
					var overrides = property.FindOverrides(storeObject);
					if (overrides is null)
						continue;

					var renamedOverrides = property.GetOrCreateOverrides(renamed);
					if (overrides.IsColumnNameOverridden)
						renamedOverrides.SetColumnName(rewriteName(overrides.ColumnName!));
					property.RemoveOverrides(storeObject);
				}

				entity.RemoveMappingFragment(storeObject);
			}

			// An explicit key name (RelationalAnnotationNames.Name) is GLOBAL per key -- EF has no
			// per-store-object override, so on a split entity one rewritten name would stamp the PK
			// constraint of the main table AND every fragment table identically. Postgres backs PK
			// constraints with schema-scoped relations, making that duplicate a hard 42P07 at migrate
			// time. Fragment-bearing entities therefore keep EF's per-table default names
			// ("PK_" + each table's already-rewritten name) -- the PascalCase "PK_" prefix on exactly
			// those constraints is the accepted cost until EF grows per-store-object key naming.
			var primaryKey = entity.FindPrimaryKey();
			if (primaryKey is not null && !hasMappingFragments)
			{
				var primaryKeyName = primaryKey.GetName();
				if (!string.IsNullOrWhiteSpace(primaryKeyName))
					primaryKey.SetName(rewriteName(primaryKeyName));
			}

			foreach (var property in entity.GetProperties())
			{
				property.SetColumnName(rewriteName(property.GetColumnName()));

				var defaultConstraintName = property.GetDefaultConstraintName();
				if (!string.IsNullOrWhiteSpace(defaultConstraintName))
					property.SetDefaultConstraintName(rewriteName(defaultConstraintName));
			}

			foreach (var complexProperty in entity.GetComplexProperties())
				RenameComplexType(complexProperty.ComplexType, rewriteName);

			// Same global-annotation hazard as the primary key above: skip explicit key names entirely
			// on fragment-bearing entities.
			if (!hasMappingFragments)
				foreach (var key in entity.GetKeys())
				{
					var keyName = key.GetName();
					if (!string.IsNullOrWhiteSpace(keyName))
						key.SetName(rewriteName(keyName));
				}

			foreach (var foreignKey in entity.GetForeignKeys())
			{
				// The row-internal linking FK EF synthesizes for entity splitting (self-referencing,
				// PK-to-PK) maps to one constraint PER fragment table, and its no-arg
				// GetConstraintName() default derives from the entity's MAIN table on both sides --
				// so pinning that as the single global explicit name would misname every fragment's
				// linking constraint (no fragment table in the name) and collide across multiple
				// fragments on engines with schema-scoped constraint names. EF's per-store defaults
				// already name each linking constraint for its own fragment table; leave them alone.
				if (hasMappingFragments
					&& foreignKey.PrincipalEntityType == entity
					&& entity.FindPrimaryKey() is { } splitKey
					&& foreignKey.Properties.SequenceEqual(splitKey.Properties))
					continue;

				var constraintName = foreignKey.GetConstraintName();
				if (!string.IsNullOrWhiteSpace(constraintName))
					foreignKey.SetConstraintName(rewriteName(constraintName));
			}

			foreach (var index in entity.GetIndexes())
			{
				var databaseName = index.GetDatabaseName();
				if (!string.IsNullOrWhiteSpace(databaseName))
					index.SetDatabaseName(rewriteName(databaseName));
			}

			applyProviderSpecificRenames?.Invoke(entity, rewriteName);
		}
	}

	/// <summary>
	///     Complex types (<c>ComplexProperty&lt;T&gt;()</c>) never appear in <c>GetEntityTypes()</c> -- they
	///     hang off their declaring entity's <c>GetComplexProperties()</c>, so the main loop above never sees
	///     them. A JSON-mapped complex property (<c>.ToJson()</c>) owns its own
	///     container column exactly like a root JSON entity does, so it gets the same treatment. A non-JSON
	///     complex property maps its scalar properties onto ordinary columns on the owning table instead.
	///     Recursion stops the moment a JSON container is found: a complex property nested inside one shares
	///     that same physical column rather than owning one of its own, so renaming it would hit the identical
	///     shaper corruption the root-vs-nested JSON entity exclusion above exists to prevent.
	/// </summary>
	static void RenameComplexType(IConventionComplexType complexType, Func<string, string> rewriteName)
	{
		var containerColumnName = complexType.GetContainerColumnName();
		if (!string.IsNullOrWhiteSpace(containerColumnName))
		{
			complexType.SetContainerColumnName(rewriteName(containerColumnName));
			return;
		}

		foreach (var property in complexType.GetProperties())
			property.SetColumnName(rewriteName(property.GetColumnName()));

		foreach (var nestedComplexProperty in complexType.GetComplexProperties())
			RenameComplexType(nestedComplexProperty.ComplexType, rewriteName);
	}
}
