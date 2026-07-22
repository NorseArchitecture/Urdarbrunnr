using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Norse.Persistence.EntityFramework.Design.SqlServer;

/// <summary>
/// Base <see cref="IDesignTimeDbContextFactory{TContext}"/> for SQL Server-backed Norse contexts, used
/// only by <c>dotnet ef</c> tooling. Wires the SQL Server provider; naming stays PascalCase by default
/// (matching <c>NorseSqlServerContextExtensions</c>' own runtime default -- SQL Server's
/// case-insensitive collation round-trips raw PascalCase fine, unlike Postgres). For the full rationale
/// behind the <see cref="ConfigureOptions"/> extension point, see the PostgreSQL design-time factory.
/// </summary>
/// <typeparam name="TContext">The Norse EF context this factory constructs at design time.</typeparam>
public abstract class NorseSqlServerDesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext>
	where TContext : DbContext, INorseDbContext
{
	/// <summary>The realm's database name -- e.g. <c>"norse_identity"</c>.</summary>
	protected abstract string DatabaseName { get; }

	/// <summary>
	/// The connection string used when <c>DOTNET_EFTOOLS_CONNECTIONSTRING</c> is not set. Points at a
	/// local dev SQL Server container by convention -- provisional until Bifröst wires a real one into
	/// the AppHost (deferred, see the design doc).
	/// </summary>
	protected virtual string DefaultConnectionString =>
		$"Server=localhost;Database={DatabaseName};User Id=sa;Password=devpassword;TrustServerCertificate=true";

	/// <summary>
	/// Whether Norse's snake_case naming convention is applied. Defaults to <see langword="false"/>,
	/// matching <c>NorseSqlServerContextExtensions</c>' own SQL Server default -- override only if the
	/// realm's runtime registration also opts in, to keep design-time scaffolding consistent with what
	/// the running container actually produces.
	/// </summary>
	protected virtual bool UseSnakeCaseNaming => false;

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
		builder.UseSqlServer(connectionString,
			o => o.MigrationsAssembly(GetType().Assembly.GetName().Name));

		if (UseSnakeCaseNaming)
			NorseDbContextOptionsExtensions.ApplyNorseConventions(builder);
	}

	/// <summary>Constructs <typeparamref name="TContext"/> from the configured options.</summary>
	protected abstract TContext CreateContext(DbContextOptions<TContext> options);
}
