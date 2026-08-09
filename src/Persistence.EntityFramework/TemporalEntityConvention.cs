using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Norse.Persistence.EntityFramework;

/// <summary>
///     Validates every <see cref="ITemporalEntity" /> at model finalize, stamps the
///     <see cref="NorseAnnotationNames.Temporal" /> annotation on its main table mapping, and reserves
///     the derived history/timeline names (fails loudly on collision). The <c>system_period</c> column
///     name is reserved too — not because SQL Server's own temporal realization ever produces a
///     column by that name (it stamps <c>SystemPeriodStart</c>/<c>SystemPeriodEnd</c> shadow
///     properties instead), but because one model builds for both providers and PostgreSQL owns
///     <c>system_period</c> in migration SQL generation, never by an application property — so a
///     mapped property whose column resolves to <c>system_period</c> (ordinal-ignore-case) throws
///     here, at model finalize, instead of surfacing as a duplicate-column failure from the
///     migration emitter. When the provider binding supplies a
///     <see cref="INorseEfProvider.TemporalRealizationHook" />, each validated entity is realized
///     immediately after its stamp — one deterministic pass, mirroring how
///     <see cref="INorseEfProvider.EntityRenameHook" /> rides the naming convention. A separate
///     finalizing convention cannot do this: plugin-added conventions enter the finalizing list before
///     context-added ones, so a realization pass registered independently races the stamp it reads.
/// </summary>
/// <param name="realizationHook">
///     The provider binding's realization hook, or <see langword="null" /> when the provider realizes
///     temporality elsewhere (Postgres: migration SQL generation, never the model).
/// </param>
sealed class TemporalEntityConvention(Action<IConventionEntityType>? realizationHook)
	: IModelFinalizingConvention
{
	public void ProcessModelFinalizing(
		IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
	{
		var entityTypes = modelBuilder.Metadata.GetEntityTypes().ToList();
		var tableNames = entityTypes
			.Select(e => e.GetTableName())
			.Concat(entityTypes
				.SelectMany(e => e.GetMappingFragments(StoreObjectType.Table))
				.Select(fragment => (string?)fragment.StoreObject.Name))
			.Where(n => n is not null)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (var entityType in entityTypes.Where(e => typeof(ITemporalEntity).IsAssignableFrom(e.ClrType)))
		{
			if (entityType.FindPrimaryKey() is null)
				throw new InvalidOperationException(
					$"Temporal entity '{entityType.DisplayName()}' has no primary key; ITemporalEntity requires one.");
			if (entityType.IsOwned() || entityType.GetContainerColumnName() is not null)
				throw new InvalidOperationException(
					$"Temporal entity '{entityType.DisplayName()}' is owned or JSON-mapped; ITemporalEntity applies to root table-mapped entities only.");

			var table = entityType.GetTableName()!;
			foreach (var derived in (string[])[$"{table}_history", $"{table}_timeline", $"{table}History"])
				if (tableNames.Contains(derived))
					throw new InvalidOperationException(
						$"Table '{derived}' collides with temporal entity '{entityType.DisplayName()}''s derived name; derived history/timeline names are reserved.");

			var rootTable = StoreObjectIdentifier.Table(table, entityType.GetSchema());
			foreach (var property in entityType.GetProperties())
			{
				var columnName = property.GetColumnName(rootTable);
				if (columnName is not null
					&& string.Equals(columnName, "system_period", StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException(
						$"Temporal entity '{entityType.DisplayName()}''s property '{property.Name}' maps to " +
						$"column '{columnName}'; 'system_period' is database-owned by the temporal apparatus " +
						"and is reserved — an application property cannot map to it.");
			}

			entityType.Builder.HasAnnotation(NorseAnnotationNames.Temporal, true);
			realizationHook?.Invoke(entityType);
		}
	}
}
