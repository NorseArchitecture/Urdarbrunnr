using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Norse.Persistence.EntityFramework.Design.SqlServer;

/// <summary>
/// Base <see cref="IDesignTimeDbContextFactory{TContext}"/> for SQL Server-backed Norse contexts, used
/// only by <c>dotnet ef</c> tooling. Wires the SQL Server provider; naming stays PascalCase by default
/// (matching <c>NorseSqlServerContextExtensions</c>' own runtime default -- SQL Server's
/// case-insensitive collation round-trips raw PascalCase fine, unlike Postgres). For the full rationale
/// behind the <see cref="ConfigureOptions"/> extension point, see the Postgres design-time factory.
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
	/// <remarks>
	/// <b>Known gap:</b> when <see langword="true"/>, this factory calls the plain
	/// <c>ApplyNorseConventions(builder)</c> overload -- it does NOT also rename a temporal entity's
	/// history table to snake_case the way the runtime path does (<c>NorseSqlServerContextExtensions</c>
	/// passes <c>RenameTemporalHistoryTable</c> into its own <c>ApplyNorseConventions</c> call). A realm
	/// that combines <see cref="UseSnakeCaseNaming"/> = <see langword="true"/> with a temporal table on
	/// SQL Server will see the design-time-scaffolded schema disagree with what the running container
	/// actually produces for that table's name, until this factory mirrors the runtime overload. Dormant
	/// today -- no Norse realm uses temporal tables yet, and this flag defaults to <see langword="false"/>.
	/// </remarks>
	protected virtual bool UseSnakeCaseNaming => false;

	/// <inheritdoc />
	public TContext CreateDbContext(string[] args)
	{
		var connectionString =
			Environment.GetEnvironmentVariable("DOTNET_EFTOOLS_CONNECTIONSTRING") ??
			DefaultConnectionString;

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
			o => o
				.MigrationsAssembly(GetType().Assembly.GetName().Name)
				// Must match NorseSqlServerContextExtensions.SqlServerCompatibilityLevel -- SQL Server
				// 2025's compatibility level, forced so dotnet-ef scaffolds JSON-mapped properties as the
				// native `json` column type instead of `nvarchar(max)`, matching what the runtime
				// registration actually produces.
				.UseCompatibilityLevel(170));

		if (UseSnakeCaseNaming)
			builder.ApplyNorseConventions();
	}

	/// <summary>Constructs <typeparamref name="TContext"/> from the configured options.</summary>
	protected abstract TContext CreateContext(DbContextOptions<TContext> options);
}
