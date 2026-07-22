using Microsoft.EntityFrameworkCore;
using Norse.Abstractions.Migrations;

namespace Norse.Persistence.EntityFramework.Migrations;

/// <summary>
/// Provider-agnostic abstract base for EF Core migration contributors. Subclasses are discovered
/// by the migrations service and executed in order during startup.
/// </summary>
/// <remarks>
/// Constrained to <c>TContext : DbContext, INorseDbContext</c> so only Norse-registered contexts
/// can be wired in. Annotate the concrete subclass with
/// <see cref="MigrationConnectionStringAttribute"/> to supply the Aspire connection-string name
/// the source generator needs.
/// </remarks>
/// <typeparam name="TContext">The Norse EF context this contributor migrates.</typeparam>
/// <param name="context">The context instance resolved from DI.</param>
public abstract class EfMigrationContributor<TContext>(TContext context) : IMigrationContributor
	where TContext : DbContext, INorseDbContext
{
	/// <inheritdoc />
	public abstract string Name { get; }

	/// <inheritdoc />
	public Task MigrateAsync(CancellationToken cancellationToken) =>
		context.Database.MigrateAsync(cancellationToken);
}
