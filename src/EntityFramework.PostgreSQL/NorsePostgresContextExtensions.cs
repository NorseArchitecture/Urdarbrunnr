using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.EntityFramework.PostgreSQL;

/// <summary>
/// Aspire-wired registration extensions for Norse EF Postgres contexts.
/// </summary>
public static class NorsePostgresContextExtensions
{
	/// <summary>
	/// Registers <typeparamref name="TContext"/> in the Aspire host using Npgsql EF Core integration,
	/// applying Norse snake_case naming conventions. The connection string is resolved by
	/// <paramref name="connectionStringName"/> from the application configuration.
	/// </summary>
	/// <typeparam name="TContext">
	/// The <see cref="DbContext"/> type to register. Must implement <see cref="INorseDbContext"/>.
	/// </typeparam>
	/// <param name="builder">The host application builder.</param>
	/// <param name="connectionStringName">The connection string name in application configuration.</param>
	/// <returns>The same <paramref name="builder"/> for chaining.</returns>
	public static IHostApplicationBuilder AddNorsePostgresContext<TContext>(
		this IHostApplicationBuilder builder,
		string connectionStringName)
		where TContext : DbContext, INorseDbContext
	{
		builder.AddNpgsqlDbContext<TContext>(connectionStringName,
			configureDbContextOptions: opts => opts.UseSnakeCaseNamingConvention());
		return builder;
	}

	/// <summary>
	/// Registers <typeparamref name="TContext"/> for one-shot, non-pooled use (migrations and other
	/// short-lived init-container work). Unlike <see cref="AddNorsePostgresContext{TContext}"/>, this
	/// does NOT pool the context — pooling is reserved for long-running runtime hosts (web server,
	/// worker); a migrations service constructs its context once and exits, so pooling only adds risk
	/// (EF Core forbids <c>OnConfiguring</c> from mutating frozen pooled options) for no benefit.
	/// Still gets Aspire's retry policy, health check, and telemetry via <c>EnrichNpgsqlDbContext</c>.
	/// </summary>
	/// <typeparam name="TContext">
	/// The <see cref="DbContext"/> type to register. Must implement <see cref="INorseDbContext"/>.
	/// </typeparam>
	/// <param name="builder">The host application builder.</param>
	/// <param name="connectionStringName">The connection string name in application configuration.</param>
	/// <param name="migrationsAssemblyName">
	/// The name of the assembly containing <typeparamref name="TContext"/>'s EF Core migrations. Norse
	/// convention places migrations in a sibling <c>*.Migrations</c> assembly, never in the context's own
	/// assembly — this must be supplied explicitly rather than inferred, since EF Core defaults to
	/// searching the context's own assembly and finds nothing there.
	/// </param>
	/// <returns>The same <paramref name="builder"/> for chaining.</returns>
	public static IHostApplicationBuilder AddNorsePostgresMigrationContext<TContext>(
		this IHostApplicationBuilder builder,
		string connectionStringName,
		string migrationsAssemblyName)
		where TContext : DbContext, INorseDbContext
	{
		var connectionString = builder.Configuration.GetConnectionString(connectionStringName)
			?? throw new InvalidOperationException($"Connection string '{connectionStringName}' was not found.");

		builder.Services.AddDbContext<TContext>(opts =>
			opts.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(migrationsAssemblyName)));
		builder.EnrichNpgsqlDbContext<TContext>();

		return builder;
	}
}
