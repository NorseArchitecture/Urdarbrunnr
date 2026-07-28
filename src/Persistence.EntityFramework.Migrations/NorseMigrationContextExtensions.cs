using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.Migrations;

/// <summary>
/// Provider-neutral migration-host registration. Constrained on
/// <see cref="INorseEfMigrationProvider"/> — the migration-host half of the provider seam — so a
/// local-dev-only provider (SQLite) cannot be pointed at a migrations host at all: the call does
/// not compile.
/// </summary>
public static class NorseMigrationContextExtensions
{
	/// <param name="builder">The host application builder.</param>
	extension(IHostApplicationBuilder builder)
	{
		/// <summary>
		/// Registers <typeparamref name="TContext"/> for one-shot, non-pooled use (migrations and
		/// other short-lived init-container work). Not pooled: a migrations service constructs its
		/// context once and exits, and EF Core forbids <c>OnConfiguring</c> mutating frozen pooled
		/// options — pooling is pure risk here. Still enriched via the binding (retry, health
		/// check, telemetry).
		/// </summary>
		/// <typeparam name="TContext">
		/// The <see cref="DbContext"/> type to register. Must implement <see cref="INorseDbContext"/>.
		/// </typeparam>
		/// <param name="provider">The provider binding (migration-capable tier).</param>
		/// <param name="connectionStringName">The connection string name in application configuration.</param>
		/// <param name="migrationsAssemblyName">
		/// The sibling assembly containing <typeparamref name="TContext"/>'s EF migrations — always
		/// supplied explicitly; EF's default of searching the context's own assembly finds nothing
		/// by Norse convention.
		/// </param>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddNorseMigrationContext<TContext>(
			INorseEfMigrationProvider provider, string connectionStringName,
			string migrationsAssemblyName)
			where TContext : DbContext, INorseDbContext
		{
			var connectionString = builder.Configuration.GetConnectionString(connectionStringName) ??
				throw new InvalidOperationException(
					$"Connection string '{connectionStringName}' was not found.");

			builder.Services.AddDbContext<TContext>(opts =>
				opts.ApplyNorseProviderOptions(provider, connectionString, migrationsAssemblyName));
			provider.Enrich<TContext>(builder);

			return builder;
		}
	}
}
