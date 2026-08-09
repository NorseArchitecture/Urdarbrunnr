using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.PostgreSQL;

/// <summary>
///     The PostgreSQL provider binding. Stateless; consume via <see cref="Instance" />. Postgres folds
///     unquoted identifiers to lowercase, so lower snake_case is the engine's own escape-free native
///     style — supplied here as binding data, not a realm lever. No forced floors today.
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
	public Func<string, string>? NameRewriter => _lowerSnakeCase;

	/// <inheritdoc />
	public Action<IConventionEntityType, Func<string, string>>? EntityRenameHook => null;

	/// <inheritdoc />
	public Action<IConventionEntityType>? TemporalRealizationHook => null;

	/// <inheritdoc />
	/// <remarks>
	///     The three <c>ReplaceService</c> calls are the whole PostgreSQL temporal seam: the relational
	///     annotation provider makes the <see cref="NorseAnnotationNames.Temporal" /> marker visible to EF's
	///     model differ, the migrations annotation provider carries that marker onto the one operation the
	///     differ builds without consulting the model — the drop — and the SQL generator emits the history
	///     apparatus around Npgsql's own migration SQL. All three are unconditional — temporality is opted
	///     into per entity, never per registration.
	/// </remarks>
	public void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString,
		string? migrationsAssemblyName) =>
		optionsBuilder
			.UseNpgsql(connectionString, npgsql =>
			{
				if (migrationsAssemblyName is not null)
					npgsql.MigrationsAssembly(migrationsAssemblyName);
			})
			.ReplaceService<IRelationalAnnotationProvider, NorseNpgsqlAnnotationProvider>()
			.ReplaceService<IMigrationsAnnotationProvider, NorseNpgsqlMigrationsAnnotationProvider>()
			.ReplaceService<IMigrationsSqlGenerator, NorseNpgsqlMigrationsSqlGenerator>();

	/// <inheritdoc />
	public void Enrich<TContext>(IHostApplicationBuilder builder)
		where TContext : DbContext, INorseDbContext =>
		builder.EnrichNpgsqlDbContext<TContext>();

	/// <inheritdoc />
	public string DesignTimePlaceholderConnectionString(string databaseName) =>
		$"Host=design;Database={databaseName};Username=design;Password=design";
}
