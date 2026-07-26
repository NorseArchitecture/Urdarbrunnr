using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Options-builder extensions that apply Norse platform-wide EF conventions: naming and, separately,
/// query tracking behavior.
/// </summary>
public static class NorseDbContextOptionsExtensions
{
	/// <summary>
	/// The stable EF Core provider identity string (what <c>Database.ProviderName</c> returns) for
	/// the SQL Server provider. Exposed so contexts that cannot inherit <see cref="NorseDbContext"/>
	/// (auth contexts inheriting <c>IdentityDbContext</c>) can compute the same provider check
	/// <see cref="NorseDbContext.ConfigureConventions"/> uses for fixed-length applicability, without
	/// duplicating the literal.
	/// </summary>
	public const string SqlServerProviderName = "Microsoft.EntityFrameworkCore.SqlServer";

	/// <param name="optionsBuilder">The options builder to configure.</param>
	extension(DbContextOptionsBuilder optionsBuilder)
	{
		/// <summary>
		/// Applies snake_case naming to all entity table names, column names, keys, foreign keys, indexes,
		/// and JSON container columns, via Urðarbrunnr's own <see cref="NorseSnakeCaseNamingConvention"/>.
		/// Called conditionally by each provider's registration extension (see
		/// <c>Norse.Persistence.EntityFramework.PostgreSQL.NorsePostgresContextExtensions</c> and its SQL Server
		/// counterpart) — never unconditionally by a context itself, since whether snake_case is the right
		/// default is a provider decision, not a Norse-wide one.
		/// </summary>
		/// <param name="applyProviderSpecificRenames">
		/// Optional provider-specific rename hook, invoked once per entity in addition to this method's own
		/// renames. Used by <c>Norse.Persistence.EntityFramework.SqlServer</c> to rename temporal history tables — an
		/// EF API this provider-neutral project must never reference directly. See
		/// <see cref="NorseSnakeCaseNamingConvention"/>'s remarks for the full rationale.
		/// </param>
		/// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
		public DbContextOptionsBuilder ApplyNorseConventions(Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames = null)
		{
			((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
				.AddOrUpdateExtension(new NorseSnakeCaseNamingOptionsExtension(applyProviderSpecificRenames));
			return optionsBuilder;
		}

		/// <summary>
		/// Forces <see cref="QueryTrackingBehavior.NoTracking"/> platform-wide. Norse Architecture's stance:
		/// change tracking and lazy loading are legacy features with no place in an event-driven, CQRS-shaped
		/// query side — a tracked query also can't project an owned entity without its owner in the result
		/// (EF Core throws at query time), so there is no scenario on this platform where tracking is the
		/// right default. Called unconditionally by every provider's registration extension (see
		/// <c>Norse.Persistence.EntityFramework.PostgreSQL.NorsePostgresContextExtensions</c> and its SQL
		/// Server counterpart) — unlike <see cref="ApplyNorseConventions"/>, this is platform law, not a
		/// provider decision, so it is never gated behind an opt-out parameter. Any context built directly
		/// (a hand-rolled <see cref="DbContextOptionsBuilder"/> in a test, for example) must call this too;
		/// a handler that genuinely needs a tracked graph opts back in per-query via <c>AsTracking()</c>.
		/// </summary>
		/// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
		public DbContextOptionsBuilder ApplyNorseTrackingBehavior() =>
			optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
	}
}
