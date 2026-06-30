using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework;

/// <summary>
/// Options-builder extensions that apply Norse platform naming conventions.
/// </summary>
public static class NorseDbContextOptionsExtensions
{
	/// <summary>
	/// Applies snake_case naming to all entity table names and column names via
	/// <c>EFCore.NamingConventions</c>. Called by <see cref="NorseDbContext"/> automatically in
	/// <c>OnConfiguring</c>; auth contexts that inherit <c>IdentityDbContext</c> call this manually
	/// in their own <c>OnConfiguring</c>.
	/// </summary>
	/// <param name="optionsBuilder">The options builder to configure.</param>
	/// <returns>The same <paramref name="optionsBuilder"/> for chaining.</returns>
	public static DbContextOptionsBuilder ApplyNorseConventions(DbContextOptionsBuilder optionsBuilder)
	{
		optionsBuilder.UseSnakeCaseNamingConvention();
		return optionsBuilder;
	}
}
