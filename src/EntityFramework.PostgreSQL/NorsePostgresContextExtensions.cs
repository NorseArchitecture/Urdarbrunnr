using Microsoft.EntityFrameworkCore;
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
}
