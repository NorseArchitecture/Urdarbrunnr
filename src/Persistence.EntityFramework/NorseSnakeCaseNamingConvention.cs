using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Model-finalizing convention that renames every relational EF metadata object to snake_case: table
/// names, primary key names, column names, default-constraint names, key names, foreign key constraint
/// names, and index names. JSON-mapped entities only have their container column name rewritten — EF
/// migrations fail if a JSON-mapped entity's table/column identity is touched the normal way, so those
/// entities short-circuit the rest of the walk.
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
sealed class NorseSnakeCaseNamingConvention(
	Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames) : IModelFinalizingConvention
{
	public void ProcessModelFinalizing(
		IConventionModelBuilder builder, IConventionContext<IConventionModelBuilder> context)
	{
		foreach (var entity in builder.Metadata.GetEntityTypes())
		{
			if (entity.IsMappedToJson())
			{
				var containerColumnName = entity.GetContainerColumnName();
				if (!string.IsNullOrWhiteSpace(containerColumnName))
					entity.SetContainerColumnName(SnakeCaseNameRewriter.RewriteName(containerColumnName));
				continue;
			}

			var tableName = entity.GetTableName();
			if (string.IsNullOrWhiteSpace(tableName))
				continue;

			entity.SetTableName(SnakeCaseNameRewriter.RewriteName(tableName));

			var primaryKey = entity.FindPrimaryKey();
			if (primaryKey is not null)
			{
				var primaryKeyName = primaryKey.GetName();
				if (!string.IsNullOrWhiteSpace(primaryKeyName))
					primaryKey.SetName(SnakeCaseNameRewriter.RewriteName(primaryKeyName));
			}

			foreach (var property in entity.GetProperties())
			{
				property.SetColumnName(SnakeCaseNameRewriter.RewriteName(property.GetColumnName()));

				var defaultConstraintName = property.GetDefaultConstraintName();
				if (!string.IsNullOrWhiteSpace(defaultConstraintName))
					property.SetDefaultConstraintName(SnakeCaseNameRewriter.RewriteName(defaultConstraintName));
			}

			foreach (var key in entity.GetKeys())
			{
				var keyName = key.GetName();
				if (!string.IsNullOrWhiteSpace(keyName))
					key.SetName(SnakeCaseNameRewriter.RewriteName(keyName));
			}

			foreach (var foreignKey in entity.GetForeignKeys())
			{
				var constraintName = foreignKey.GetConstraintName();
				if (!string.IsNullOrWhiteSpace(constraintName))
					foreignKey.SetConstraintName(SnakeCaseNameRewriter.RewriteName(constraintName));
			}

			foreach (var index in entity.GetIndexes())
			{
				var databaseName = index.GetDatabaseName();
				if (!string.IsNullOrWhiteSpace(databaseName))
					index.SetDatabaseName(SnakeCaseNameRewriter.RewriteName(databaseName));
			}

			applyProviderSpecificRenames?.Invoke(entity, SnakeCaseNameRewriter.RewriteName);
		}
	}
}
