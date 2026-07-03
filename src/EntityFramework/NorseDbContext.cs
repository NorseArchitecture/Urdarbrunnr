using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework;

/// <summary>
/// Abstract <see cref="DbContext"/> base for all non-auth Norse EF contexts. Applies snake_case naming
/// conventions via <see cref="NorseDbContextOptionsExtensions.ApplyNorseConventions"/> during context
/// configuration. Auth contexts inherit <c>IdentityDbContext</c> instead and call
/// <see cref="NorseDbContextOptionsExtensions.ApplyNorseConventions"/> manually in their <c>OnConfiguring</c>.
/// </summary>
/// <param name="options">The options for this context.</param>
public abstract class NorseDbContext(DbContextOptions options) : DbContext(options), INorseDbContext
{
	/// <inheritdoc />
	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		base.OnConfiguring(optionsBuilder);
		NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder);
	}

	/// <inheritdoc />
	protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
	{
		base.ConfigureConventions(configurationBuilder);
		NorseModelConventions.Apply(configurationBuilder);
	}
}
