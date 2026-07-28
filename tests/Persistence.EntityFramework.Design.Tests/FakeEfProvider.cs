using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.Design.Tests;

sealed class FakeEfProvider : INorseEfMigrationProvider
{
	public string? SeenConnectionString { get; private set; }
	public string? SeenMigrationsAssemblyName { get; private set; }
	public bool MigrationsAssemblySeen { get; private set; }
	public int EnrichCalls { get; private set; }

	public Func<string, string>? NameRewriter { get; init; }

	public Action<IConventionEntityType, Func<string, string>>? EntityRenameHook { get; init; }

	public void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString,
		string? migrationsAssemblyName)
	{
		SeenConnectionString = connectionString;
		SeenMigrationsAssemblyName = migrationsAssemblyName;
		MigrationsAssemblySeen = true;
		optionsBuilder.UseSqlite(connectionString);
	}

	public void Enrich<TContext>(IHostApplicationBuilder builder)
		where TContext : DbContext, INorseDbContext =>
		EnrichCalls++;

	public string DesignTimePlaceholderConnectionString(string databaseName) =>
		$"Data Source={databaseName}.design.db";
}
