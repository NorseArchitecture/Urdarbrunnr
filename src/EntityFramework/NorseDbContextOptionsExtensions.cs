using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework;

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

	/// <summary>
	/// Applies snake_case naming to all entity table names and column names via
	/// <c>EFCore.NamingConventions</c>. Called conditionally by each provider's registration
	/// extension (see <c>Norse.EntityFramework.PostgreSQL.NorsePostgresContextExtensions</c> and its
	/// SQL Server counterpart) — never unconditionally by a context itself, since whether snake_case
	/// is the right default is a provider decision, not a Norse-wide one.
	/// </summary>
	/// <param name="optionsBuilder">The options builder to configure.</param>
	/// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
	public static DbContextOptionsBuilder ApplyNorseConventions(DbContextOptionsBuilder optionsBuilder)
	{
		optionsBuilder.UseSnakeCaseNamingConvention();
		return optionsBuilder;
	}
}
