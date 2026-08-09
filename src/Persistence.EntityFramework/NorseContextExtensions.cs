using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework;

/// <summary>
///     Provider-neutral runtime registration for Norse EF contexts. The provider binding supplies every
///     provider-varying fact; the remaining levers on a registration are exactly two — the connection
///     string name and the context type.
/// </summary>
public static class NorseContextExtensions
{
	/// <param name="builder">The host application builder.</param>
	extension(IHostApplicationBuilder builder)
	{
		/// <summary>
		///     Registers <typeparamref name="TContext" /> pooled, with the binding's provider
		///     configuration, the platform no-tracking law, binding-derived naming, and the binding's
		///     Aspire enrichment (retry, health check, telemetry). Pooling uses EF's own
		///     <c>AddDbContextPool</c> + the provider's <c>Enrich</c> — Aspire's documented equivalent
		///     of its <c>Add{Provider}DbContext</c> sugar, keeping the <c>Aspire:*</c> settings sections
		///     in force.
		/// </summary>
		/// <typeparam name="TContext">
		///     The <see cref="DbContext" /> type to register. Must implement <see cref="INorseDbContext" />.
		/// </typeparam>
		/// <param name="provider">The provider binding.</param>
		/// <param name="connectionStringName">The connection string name in application configuration.</param>
		/// <returns>The same <paramref name="builder" /> for chaining.</returns>
		public IHostApplicationBuilder AddNorseContext<TContext>(INorseEfProvider provider,
			string connectionStringName)
			where TContext : DbContext, INorseDbContext
		{
			var connectionString = builder.Configuration.GetConnectionString(connectionStringName) ??
				throw new InvalidOperationException(
					$"Connection string '{connectionStringName}' was not found.");

			builder.Services.AddDbContextPool<TContext>(opts =>
				opts.ApplyNorseProviderOptions(provider, connectionString, migrationsAssemblyName: null));
			provider.Enrich<TContext>(builder);

			return builder;
		}

		/// <summary>
		///     Registers <typeparamref name="TContext" /> as a pooled <see cref="IDbContextFactory{TContext}" /> —
		///     the runtime DI shape Midgard's generic well repository needs (create-execute-dispose per
		///     operation), as opposed to <see cref="AddNorseContext{TContext}" />'s directly-injectable pooled
		///     context (the shape ASP.NET Core Identity's built-in stores require instead). Same provider seam,
		///     same enrichment, same fail-fast-on-missing-connection-string behavior as its sibling — the DI
		///     registration call is the only thing that differs.
		/// </summary>
		/// <param name="provider">The provider binding.</param>
		/// <param name="connectionStringName">The configuration key under <c>ConnectionStrings</c>.</param>
		/// <returns>The same <paramref name="builder" /> for chaining.</returns>
		/// <exception cref="InvalidOperationException"><paramref name="connectionStringName" /> is not configured.</exception>
		public IHostApplicationBuilder AddNorseContextFactory<TContext>(INorseEfProvider provider,
			string connectionStringName)
			where TContext : DbContext, INorseDbContext
		{
			var connectionString = builder.Configuration.GetConnectionString(connectionStringName) ??
				throw new InvalidOperationException(
					$"Connection string '{connectionStringName}' was not found.");

			builder.Services.AddPooledDbContextFactory<TContext>(opts =>
				opts.ApplyNorseProviderOptions(provider, connectionString, migrationsAssemblyName: null));
			provider.Enrich<TContext>(builder);

			return builder;
		}
	}
}
