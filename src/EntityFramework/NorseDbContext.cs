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

	/// <inheritdoc />
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		ConfigureNorseEntities(modelBuilder);
	}

	/// <summary>
	/// Empty by default. A Tier-1 consumer project declares its own <c>DbContext</c> subclass
	/// <c>partial</c> — EntityConfigurationApplicationGenerator (in that project's own
	/// compilation, alongside its <c>INorseEntity&lt;TSelf&gt;</c> entities) emits a second partial
	/// declaration overriding this method. Real virtual dispatch, not a generated static extension call —
	/// see the plan's "Design amendments" note on why the static-extension approach can't work for a base
	/// class compiled once and shipped as a package.
	/// </summary>
	protected virtual void ConfigureNorseEntities(ModelBuilder modelBuilder)
	{
	}
}
