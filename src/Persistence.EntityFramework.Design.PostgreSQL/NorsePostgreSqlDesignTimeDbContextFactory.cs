using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Norse.Persistence.EntityFramework.Design.PostgreSQL;

/// <summary>
/// Base <see cref="IDesignTimeDbContextFactory{TContext}"/> for Postgres-backed Norse contexts, used
/// only by <c>dotnet ef</c> tooling. Wires the Npgsql provider and Norse's snake_case naming
/// convention (this factory only ever targets Postgres, so applying it unconditionally is correct --
/// unlike the ambiguity <c>NorsePostgresContextExtensions</c>' runtime registration gates behind
/// <c>useSnakeCaseNaming</c>). <see cref="ConfigureOptions"/> is a second, narrower override point
/// than <see cref="CreateContext"/> alone provides -- a subclass whose context needs to configure the
/// <see cref="DbContextOptionsBuilder{TContext}"/> itself before <c>.Options</c> is built (e.g. an
/// ASP.NET Core Identity-style context calling <c>UseApplicationServiceProvider</c> to control schema
/// version) overrides it and calls <c>base.ConfigureOptions(...)</c> rather than reimplementing the
/// provider/connection-string/naming wiring from scratch.
/// </summary>
/// <typeparam name="TContext">The Norse EF context this factory constructs at design time.</typeparam>
public abstract class NorsePostgreSqlDesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext>
	where TContext : DbContext, INorseDbContext
{
	/// <summary>The realm's database name -- e.g. <c>"norse_referencedata"</c>.</summary>
	protected abstract string DatabaseName { get; }

	/// <summary>
	/// The connection string used when <c>DOTNET_EFTOOLS_CONNECTIONSTRING</c> is not set. Points at
	/// the local dev Postgres container by convention.
	/// </summary>
	protected virtual string DefaultConnectionString =>
		$"Host=localhost;Port=5432;Database={DatabaseName};Username=postgres;Password=devpassword";

	/// <summary>
	/// Whether Norse's snake_case naming convention is applied. Defaults to <see langword="true"/>,
	/// matching <c>NorsePostgresContextExtensions</c>' own Postgres default -- override only if the
	/// realm's runtime registration also opts out, to keep design-time scaffolding consistent with
	/// what the running container actually produces.
	/// </summary>
	protected virtual bool UseSnakeCaseNaming => true;

	/// <inheritdoc />
	public TContext CreateDbContext(string[] args)
	{
		var connectionString =
			Environment.GetEnvironmentVariable("DOTNET_EFTOOLS_CONNECTIONSTRING")
			?? DefaultConnectionString;

		DbContextOptionsBuilder<TContext> optionsBuilder = new();
		ConfigureOptions(optionsBuilder, connectionString);

		return CreateContext(optionsBuilder.Options);
	}

	/// <summary>
	/// Configures the options builder -- provider, connection string, and (conditionally) naming
	/// conventions. Override to layer in additional configuration; call <c>base.ConfigureOptions(...)</c>
	/// first unless deliberately replacing the base wiring entirely.
	/// </summary>
	protected virtual void ConfigureOptions(DbContextOptionsBuilder<TContext> builder, string connectionString)
	{
		builder.UseNpgsql(connectionString,
			o => o.MigrationsAssembly(GetType().Assembly.GetName().Name));

		if (UseSnakeCaseNaming)
			NorseDbContextOptionsExtensions.ApplyNorseConventions(builder);
	}

	/// <summary>Constructs <typeparamref name="TContext"/> from the configured options.</summary>
	protected abstract TContext CreateContext(DbContextOptions<TContext> options);
}
