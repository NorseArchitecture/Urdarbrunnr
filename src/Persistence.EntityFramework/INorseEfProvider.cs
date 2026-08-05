using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// The provider binding — the single seam through which all provider knowledge enters the Norse EF
/// chassis. One sealed, stateless implementation ships per provider package
/// (<c>NorsePostgresEfProvider</c>, <c>NorseSqlServerEfProvider</c>, ...), exposed as a
/// <c>public static Instance</c> singleton by convention (the migrations generator enforces the
/// convention with a compile-time diagnostic). The neutral choreography
/// (<see cref="NorseDbContextOptionsExtensions.ApplyNorseProviderOptions"/>) is the only consumer;
/// realms never implement or invoke this contract directly. Everything here is derived from the
/// provider choice — naming, floors, placeholders — never configured per realm.
/// </summary>
public interface INorseEfProvider
{
	/// <summary>
	/// Applies the provider's <c>Use{Provider}</c> call, including any forced floors (SQL Server
	/// chains its compatibility-level floor here unconditionally). <paramref name="migrationsAssemblyName"/>
	/// is <see langword="null"/> on the pooled runtime path and supplied on migration and design-time
	/// paths — Norse convention places migrations in sibling assemblies EF cannot infer.
	/// </summary>
	/// <param name="optionsBuilder">The options builder to configure.</param>
	/// <param name="connectionString">The already-resolved connection string.</param>
	/// <param name="migrationsAssemblyName">The migrations assembly, when this registration runs migrations.</param>
	void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString,
		string? migrationsAssemblyName);

	/// <summary>
	/// Applies the provider's Aspire enrichment (retry policy, health check, telemetry) to an
	/// already-registered context. Generic because the underlying Aspire
	/// <c>Enrich{Provider}DbContext&lt;TContext&gt;</c> extensions are — which is also why this seam
	/// is a contract and not a delegate: open-generic delegates do not exist.
	/// </summary>
	/// <typeparam name="TContext">The registered context type.</typeparam>
	/// <param name="builder">The host application builder.</param>
	void Enrich<TContext>(IHostApplicationBuilder builder)
		where TContext : DbContext, INorseDbContext;

	/// <summary>
	/// The engine-native identifier rewriter (<see cref="NorseNameRewriters"/>), or
	/// <see langword="null"/> to keep EF's raw names. Binding data, not a realm lever — Postgres
	/// supplies lower snake_case, SQL Server supplies none.
	/// </summary>
	Func<string, string>? NameRewriter { get; }

	/// <summary>
	/// Optional provider-specific per-entity rename hook, invoked by the naming convention alongside
	/// its own renames (SQL Server's temporal history-table rename). Only meaningful when
	/// <see cref="NameRewriter"/> is non-null — the choreography never applies the naming convention
	/// without a rewriter, so a hook paired with a null rewriter is inert by construction.
	/// </summary>
	Action<IConventionEntityType, Func<string, string>>? EntityRenameHook { get; }

	/// <summary>
	/// Invoked by <c>TemporalEntityConvention</c> once per validated temporal entity at model
	/// finalize, immediately after its <see cref="NorseAnnotationNames.Temporal"/> stamp (SQL Server:
	/// native <c>IsTemporal</c>, period/history naming, and the split-table park guard).
	/// <see langword="null"/> when the provider realizes temporality outside the model (Postgres:
	/// migration SQL generation). No default: like <see cref="EntityRenameHook"/>, every binding
	/// states its posture rather than silently inheriting a no-op.
	/// </summary>
	Action<IConventionEntityType>? TemporalRealizationHook { get; }

	/// <summary>
	/// A syntactically valid, semantically inert connection string for offline design-time model
	/// building (<c>dotnet ef migrations add</c>/<c>remove</c> never open a connection). Points at
	/// nothing; design tooling must never dial infrastructure.
	/// </summary>
	/// <param name="databaseName">The realm's database name, e.g. <c>norse_reference</c>.</param>
	/// <returns>The placeholder connection string.</returns>
	string DesignTimePlaceholderConnectionString(string databaseName);
}
