using Microsoft.EntityFrameworkCore;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Abstract <see cref="DbContext"/> base for all non-auth Norse EF contexts. Naming conventions are
/// decided by the provider registration extension used to register a context (see
/// <c>Norse.Persistence.EntityFramework.PostgreSQL.NorsePostgresContextExtensions</c> and its SQL Server
/// counterpart), never here — this base stays provider-neutral. Auth contexts inherit
/// <c>IdentityDbContext</c> instead of this class and replicate its conventions manually.
/// </summary>
/// <param name="options">The options for this context.</param>
public abstract class NorseDbContext(DbContextOptions options) : DbContext(options), INorseDbContext
{
	/// <inheritdoc />
	protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
	{
		base.ConfigureConventions(configurationBuilder);

		// Fixed-length storage (char(n)/nchar(n)) only pays off on SQL Server. Postgres's own docs
		// say character(n) has no storage/performance benefit over character varying(n) there, and
		// is usually the slower of the two — see FixedLengthAttribute's remarks.
		NorseModelConventions.Apply(configurationBuilder,
			applyFixedLength: Database.ProviderName == NorseDbContextOptionsExtensions.SqlServerProviderName);
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
