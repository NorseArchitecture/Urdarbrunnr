using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.PostgreSQL;

/// <summary>
/// Aspire-wired registration extensions for Norse EF Postgres contexts.
/// </summary>
public static class NorsePostgresContextExtensions
{
	/// <param name="builder">The host application builder.</param>
	extension(IHostApplicationBuilder builder)
	{
		/// <summary>
		/// Registers <typeparamref name="TContext"/> in the Aspire host using Npgsql EF Core integration.
		/// The connection string is resolved by <paramref name="connectionStringName"/> from the
		/// application configuration.
		/// </summary>
		/// <typeparam name="TContext">
		/// The <see cref="DbContext"/> type to register. Must implement <see cref="INorseDbContext"/>.
		/// </typeparam>
		/// <param name="connectionStringName">The connection string name in application configuration.</param>
		/// <param name="useSnakeCaseNaming">
		/// Whether to apply snake_case table/column naming. Defaults to <see langword="true"/>: Postgres
		/// folds unquoted identifiers to lowercase, so snake_case is this engine's own escape-free
		/// native style, not an opinionated override being imposed on it. Pass <see langword="false"/>
		/// to opt out and keep EFs raw (quoted) PascalCase naming instead.
		/// </param>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddNorsePostgresContext<TContext>(string connectionStringName,
			bool useSnakeCaseNaming = true)
			where TContext : DbContext, INorseDbContext
		{
			builder.AddNpgsqlDbContext<TContext>(connectionStringName,
				configureDbContextOptions: opts =>
				{
					if (useSnakeCaseNaming)
						opts.ApplyNorseConventions();
				});
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
		/// <param name="connectionStringName">The connection string name in application configuration.</param>
		/// <param name="migrationsAssemblyName">
		/// The name of the assembly containing <typeparamref name="TContext"/>'s EF Core migrations. Norse
		/// convention places migrations in a sibling <c>*.Migrations</c> assembly, never in the context's own
		/// assembly — this must be supplied explicitly rather than inferred, since EF Core defaults to
		/// searching the context's own assembly and finds nothing there.
		/// </param>
		/// <param name="useSnakeCaseNaming">See <see cref="AddNorsePostgresContext{TContext}"/>.</param>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddNorsePostgresMigrationContext<TContext>(string connectionStringName,
			string migrationsAssemblyName,
			bool useSnakeCaseNaming = true)
			where TContext : DbContext, INorseDbContext
		{
			var connectionString = builder.Configuration.GetConnectionString(connectionStringName) ??
				throw new InvalidOperationException($"Connection string '{connectionStringName}' was not found.");

			builder.Services.AddDbContext<TContext>(opts =>
			{
				opts.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(migrationsAssemblyName));
				if (useSnakeCaseNaming)
					opts.ApplyNorseConventions();
			});
			builder.EnrichNpgsqlDbContext<TContext>();

			return builder;
		}
	}
}
