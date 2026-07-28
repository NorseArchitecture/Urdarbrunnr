using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.PostgreSQL;

/// <summary>
/// The PostgreSQL provider binding. Stateless; consume via <see cref="Instance"/>. Postgres folds
/// unquoted identifiers to lowercase, so lower snake_case is the engine's own escape-free native
/// style — supplied here as binding data, not a realm lever. No forced floors today.
/// </summary>
public sealed class NorsePostgresEfProvider : INorseEfMigrationProvider
{
	static readonly Func<string, string> _lowerSnakeCase = NorseNameRewriters.LowerSnakeCase;

	NorsePostgresEfProvider()
	{
	}

	/// <summary>The well-known singleton — the "enum value" for this provider.</summary>
	public static NorsePostgresEfProvider Instance { get; } = new();

	/// <inheritdoc />
	public void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString,
		string? migrationsAssemblyName) =>
		optionsBuilder.UseNpgsql(connectionString, npgsql =>
		{
			if (migrationsAssemblyName is not null)
				npgsql.MigrationsAssembly(migrationsAssemblyName);
		});

	/// <inheritdoc />
	public void Enrich<TContext>(IHostApplicationBuilder builder)
		where TContext : DbContext, INorseDbContext =>
		builder.EnrichNpgsqlDbContext<TContext>();

	/// <inheritdoc />
	public Func<string, string>? NameRewriter => _lowerSnakeCase;

	/// <inheritdoc />
	public Action<IConventionEntityType, Func<string, string>>? EntityRenameHook => null;

	/// <inheritdoc />
	public string DesignTimePlaceholderConnectionString(string databaseName) =>
		$"Host=design;Database={databaseName};Username=design;Password=design";
}
