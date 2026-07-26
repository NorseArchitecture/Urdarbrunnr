using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.SqlServer;

/// <summary>
/// Aspire-wired registration extensions for Norse EF SQL Server contexts. Every context registered here gets
/// <see cref="QueryTrackingBehavior.NoTracking"/> forced unconditionally (not an opt-in) — the platform's
/// CQRS query side never mutates through a tracked graph, and tracking an owned-entity projection without
/// its owner throws at query time regardless, so there is no scenario where tracking is the right default.
/// A handler that genuinely needs a tracked entity opts back in per-query via <c>AsTracking()</c>.
/// </summary>
public static class NorseSqlServerContextExtensions
{
	/// <summary>
	/// SQL Server 2025's compatibility level -- the platform's floor for every SqlServer-backed context,
	/// forced unconditionally (not an opt-in, unlike <c>useSnakeCaseNaming</c>) so EF Core maps
	/// JSON-mapped properties (<c>ComplexProperty&lt;T&gt;().ToJson()</c>, <c>OwnsOne(...).ToJson()</c>)
	/// to the native <c>json</c> column type instead of <c>nvarchar(max)</c>. Client-side only -- EF Core
	/// never emits <c>ALTER DATABASE ... SET COMPATIBILITY_LEVEL</c>, so the target instance must
	/// genuinely be SQL Server 2025+ (or Azure SQL, which already defaults its own compatibility level to
	/// 170) or the generated DDL fails to apply. Must match
	/// <c>NorseSqlServerDesignTimeDbContextFactory</c>'s own compatibility level, or the scaffolded schema
	/// disagrees with what the running container actually produces.
	/// </summary>
	const int SqlServerCompatibilityLevel = 170;

	/// <param name="builder">The host application builder.</param>
	extension(IHostApplicationBuilder builder)
	{
		/// <summary>
		/// Registers <typeparamref name="TContext"/> in the Aspire host using the SQL Server EF Core
		/// integration. The connection string is resolved by <paramref name="connectionStringName"/>
		/// from the application configuration.
		/// </summary>
		/// <typeparam name="TContext">
		/// The <see cref="DbContext"/> type to register. Must implement <see cref="INorseDbContext"/>.
		/// </typeparam>
		/// <param name="connectionStringName">The connection string name in application configuration.</param>
		/// <param name="useSnakeCaseNaming">
		/// Whether to apply snake_case table/column naming. Defaults to <see langword="false"/>: SQL
		/// Server's default collation is case-insensitive, so its own raw PascalCase naming already
		/// round-trips without quoting or escaping -- unlike Postgres, there is no engine-native reason
		/// to prefer snake_case here. Pass <see langword="true"/> to opt in anyway (e.g. a deployment
		/// that wants one naming style across both a Postgres and a SQL Server target).
		/// When <see langword="true"/>, a temporal entity's history table name is renamed too — see
		/// <see cref="RenameTemporalHistoryTable"/>.
		/// </param>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddNorseSqlServerContext<TContext>(string connectionStringName,
			bool useSnakeCaseNaming = false)
			where TContext : DbContext, INorseDbContext
		{
			builder.AddSqlServerDbContext<TContext>(connectionStringName,
				configureDbContextOptions: opts =>
				{
					opts.UseSqlServer(sql => sql.UseCompatibilityLevel(SqlServerCompatibilityLevel));
					opts.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
					if (useSnakeCaseNaming)
						opts.ApplyNorseConventions(RenameTemporalHistoryTable);
				});
			return builder;
		}

		/// <summary>
		/// Registers <typeparamref name="TContext"/> for one-shot, non-pooled use (migrations and other
		/// short-lived init-container work). Unlike <see cref="AddNorseSqlServerContext{TContext}"/>,
		/// this does NOT pool the context — pooling is reserved for long-running runtime hosts (web
		/// server, worker); a migrations service constructs its context once and exits, so pooling only
		/// adds risk (EF Core forbids <c>OnConfiguring</c> from mutating frozen pooled options) for no
		/// benefit. Still gets Aspire's retry policy, health check, and telemetry via
		/// <c>EnrichSqlServerDbContext</c>.
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
		/// <param name="useSnakeCaseNaming">See <see cref="AddNorseSqlServerContext{TContext}"/>, including the temporal history table note.</param>
		/// <returns>The same <paramref name="builder"/> for chaining.</returns>
		public IHostApplicationBuilder AddNorseSqlServerMigrationContext<TContext>(string connectionStringName,
			string migrationsAssemblyName,
			bool useSnakeCaseNaming = false)
			where TContext : DbContext, INorseDbContext
		{
			var connectionString = builder.Configuration.GetConnectionString(connectionStringName) ??
				throw new InvalidOperationException($"Connection string '{connectionStringName}' was not found.");

			builder.Services.AddDbContext<TContext>(opts =>
			{
				opts.UseSqlServer(connectionString, sql => sql
					.MigrationsAssembly(migrationsAssemblyName)
					.UseCompatibilityLevel(SqlServerCompatibilityLevel));
				opts.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
				if (useSnakeCaseNaming)
					opts.ApplyNorseConventions(RenameTemporalHistoryTable);
			});
			builder.EnrichSqlServerDbContext<TContext>();

			return builder;
		}
	}

	/// <summary>
	/// Renames a temporal entity's history table to snake_case. <c>IsTemporal()</c> and
	/// <c>GetHistoryTableName()</c>/<c>SetHistoryTableName()</c> are SQL-Server-only EF APIs
	/// (<c>Microsoft.EntityFrameworkCore.SqlServerEntityTypeExtensions</c>) — this is the only project
	/// in the platform allowed to reference them; <c>Norse.Persistence.EntityFramework</c> stays provider-neutral
	/// and only ever sees this method as an opaque injected action.
	/// </summary>
	static void RenameTemporalHistoryTable(IConventionEntityType entity, Func<string, string> rewrite)
	{
		if (!entity.IsTemporal())
			return;

		var historyTableName = entity.GetHistoryTableName();
		if (!string.IsNullOrWhiteSpace(historyTableName))
			entity.SetHistoryTableName(rewrite(historyTableName));
	}
}
