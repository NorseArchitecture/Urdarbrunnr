using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.SqlServer;

/// <summary>
/// The SQL Server provider binding. Stateless; consume via <see cref="Instance"/>. SQL Server's
/// default collation is case-insensitive, so raw PascalCase is already the engine-native,
/// escape-free style — no rewriter. The 2025 compatibility-level floor is forced unconditionally
/// in <see cref="Configure"/>; it is a floor, not a lever.
/// </summary>
public sealed class NorseSqlServerEfProvider : INorseEfMigrationProvider
{
	/// <summary>
	/// SQL Server 2025's compatibility level -- the platform's floor for every SqlServer-backed context,
	/// forced unconditionally in <see cref="Configure"/> — a floor, not a lever, so no caller can opt out
	/// of it — so EF Core maps JSON-mapped properties (<c>ComplexProperty&lt;T&gt;().ToJson()</c>,
	/// <c>OwnsOne(...).ToJson()</c>) to the native <c>json</c> column type instead of
	/// <c>nvarchar(max)</c>. Client-side only -- EF Core
	/// never emits <c>ALTER DATABASE ... SET COMPATIBILITY_LEVEL</c>, so the target instance must
	/// genuinely be SQL Server 2025+ (or Azure SQL, which already defaults its own compatibility level to
	/// 170) or the generated DDL fails to apply.
	/// </summary>
	const int SqlServerCompatibilityLevel = 170;

	NorseSqlServerEfProvider()
	{
	}

	/// <summary>The well-known singleton — the "enum value" for this provider.</summary>
	public static NorseSqlServerEfProvider Instance { get; } = new();

	/// <inheritdoc />
	public void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString,
		string? migrationsAssemblyName) =>
		optionsBuilder.UseSqlServer(connectionString, sql =>
		{
			sql.UseCompatibilityLevel(SqlServerCompatibilityLevel);
			if (migrationsAssemblyName is not null)
				sql.MigrationsAssembly(migrationsAssemblyName);
		});

	/// <inheritdoc />
	public void Enrich<TContext>(IHostApplicationBuilder builder)
		where TContext : DbContext, INorseDbContext =>
		builder.EnrichSqlServerDbContext<TContext>();

	/// <inheritdoc />
	public Func<string, string>? NameRewriter => null;

	/// <inheritdoc />
	public Action<IConventionEntityType, Func<string, string>>? EntityRenameHook =>
		RenameTemporalHistoryTable;

	/// <inheritdoc />
	public Action<IConventionEntityType>? TemporalRealizationHook =>
		static entityType =>
		{
			var isSplit = entityType.GetMappingFragments().Any();
			var isParked = entityType.FindAnnotation(NorseAnnotationNames.TemporalParkedOnSqlServer) is { Value: true };
			if (isSplit && !isParked)
				throw new InvalidOperationException($$"""Temporal entity '{{entityType.DisplayName()}}' uses table splitting; EF cannot scope SQL Server temporality per fragment (dotnet/efcore#26457) and migration generation would fail (#30366). Declare TemporalParkedOnSqlServer() in Configure to acknowledge the SQL-Server-only park, or unsplit the entity.""");
			if (isSplit)
				return;
			entityType.SetIsTemporal(true);
			entityType.SetPeriodStartPropertyName("SystemPeriodStart");
			entityType.SetPeriodEndPropertyName("SystemPeriodEnd");
			entityType.SetHistoryTableName($"{entityType.GetTableName()}History");
		};

	/// <inheritdoc />
	public string DesignTimePlaceholderConnectionString(string databaseName) =>
		$"Server=design;Database={databaseName};User Id=design;Password=design;TrustServerCertificate=true";

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
