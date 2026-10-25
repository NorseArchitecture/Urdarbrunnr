using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework;

/// <summary>Temporal declarations made per entity in its static Configure.</summary>
public static class TemporalEntityTypeBuilderExtensions
{
	extension<TEntity>(EntityTypeBuilder<TEntity> builder) where TEntity : class
	{
		/// <summary>
		///     Declares that this split temporal entity versions its principal table only; its split fragments
		///     are never versioned. On SQL Server the entity is deliberately <em>not</em> EF-temporal — EF
		///     cannot scope temporality per fragment (dotnet/efcore#26457) — so the chassis emits engine-native
		///     system-versioning with <c>HIDDEN</c> period columns for the principal table instead. The cost is
		///     no <c>TemporalAsOf</c>/<c>TemporalAll</c> LINQ on SQL Server for this entity. A no-op on
		///     PostgreSQL, whose apparatus already versions the principal table only. On SQL Server, declaring it
		///     on an entity that is not entity-split throws at model finalize — an unsplit entity is EF-native
		///     temporal already, so the opt-in there is always a mistake. Call sites survive the day upstream
		///     ships per-fragment control; only this opt-in's realization changes.
		/// </summary>
		public EntityTypeBuilder<TEntity> TemporalOnPrincipalTableOnly() =>
			builder.HasAnnotation(NorseAnnotationNames.TemporalPrincipalTableOnly, true);
	}
}
