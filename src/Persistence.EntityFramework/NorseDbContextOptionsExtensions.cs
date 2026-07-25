using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Options-builder extensions that apply Norse platform naming conventions.
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
	}
}
