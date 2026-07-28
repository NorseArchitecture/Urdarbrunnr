using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norse.Persistence.EntityFramework.Migrations;

namespace Norse.Persistence.EntityFramework.SqlServer.Tests;

public sealed class NorseSqlServerEfProviderTests
{
	const string ConnectionString =
		"Server=localhost;Database=test;Trusted_Connection=True;TrustServerCertificate=True;";

	static HostApplicationBuilder CreateBuilder()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });
		return builder;
	}

	[Fact]
	void AddNorseContext_covers_every_service_Aspires_AddSqlServerDbContext_registers()
	{
		// Same Aspire-equivalence gate and same failure protocol as the Postgres binding test —
		// investigate, justify exclusions in writing, halt on a load-bearing difference.
		var aspire = CreateBuilder();
		aspire.AddSqlServerDbContext<TestContext>("test-db");

		var norse = CreateBuilder();
		norse.AddNorseContext<TestContext>(NorseSqlServerEfProvider.Instance, "test-db");

		var aspireTypes = aspire.Services.Select(d => d.ServiceType).ToHashSet();
		var norseTypes = norse.Services.Select(d => d.ServiceType).ToHashSet();
		aspireTypes.Except(norseTypes).ShouldBeEmpty();
	}

	[Fact]
	void AddNorseMigrationContext_registers_TContext_non_pooled_and_does_not_throw_building_the_model()
	{
		var builder = CreateBuilder();

		builder.AddNorseMigrationContext<TestContext>(NorseSqlServerEfProvider.Instance, "test-db",
			"Norse.Persistence.EntityFramework.SqlServer.Tests");

		var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
		descriptor.ImplementationType.ShouldBe(typeof(TestContext));
		descriptor.ImplementationFactory.ShouldBeNull();

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
		Should.NotThrow(() => _ = ctx.Model);
	}

	[Fact]
	void Configure_forces_the_2025_compatibility_level_floor_unconditionally()
	{
		DbContextOptionsBuilder<TestContext> optionsBuilder = new();

		NorseSqlServerEfProvider.Instance.Configure(optionsBuilder, ConnectionString,
			migrationsAssemblyName: null);

		// EF1001 (internal EF API): SqlServerOptionsExtension is the only observable carrier of
		// UseCompatibilityLevel's value; asserting the platform floor is exactly what this test
		// exists for, and the alternative (generating SQL against a live server) violates the
		// no-database law. Wrong-in-context, hence inline per the Suppression Law.
#pragma warning disable EF1001
		var extension = optionsBuilder.Options.FindExtension<SqlServerOptionsExtension>();
		extension.ShouldNotBeNull();
		// The engine-generic "CompatibilityLevel" property does not exist on this preview's
		// SqlServerOptionsExtension -- it split into per-engine properties (SqlServer/AzureSql/
		// AzureSynapse), and sql.UseCompatibilityLevel(...) (SqlServerDbContextOptionsBuilder, the
		// plain-SQL-Server overload Configure calls) sets SqlServerCompatibilityLevel specifically.
		extension.SqlServerCompatibilityLevel.ShouldBe(170);
#pragma warning restore EF1001
	}

	[Fact]
	void Configure_forwards_the_migrations_assembly_when_supplied()
	{
		DbContextOptionsBuilder<TestContext> optionsBuilder = new();

		NorseSqlServerEfProvider.Instance.Configure(optionsBuilder, ConnectionString,
			"Test.Migrations.Assembly");

#pragma warning disable EF1001 // same justification as above
		var extension = optionsBuilder.Options.FindExtension<SqlServerOptionsExtension>();
		extension.ShouldNotBeNull();
		extension.MigrationsAssembly.ShouldBe("Test.Migrations.Assembly");
#pragma warning restore EF1001
	}

	[Fact]
	void Binding_keeps_engine_native_PascalCase_but_pairs_the_temporal_hook_for_a_naming_binding()
	{
		// SQL Server's case-insensitive collation round-trips raw PascalCase without quoting, so no
		// rewriter — and with no rewriter the choreography never applies the naming convention, so
		// the paired hook is inert by construction. It stays wired so any future binding variant
		// that enables renaming on SQL Server inherits the history-table rename instead of
		// rediscovering the drift bug the old design-time factory had.
		NorseSqlServerEfProvider.Instance.NameRewriter.ShouldBeNull();
		NorseSqlServerEfProvider.Instance.EntityRenameHook.ShouldNotBeNull();
	}

	[Fact]
	void EntityRenameHook_renames_a_temporal_entitys_history_table_when_a_rewriter_is_supplied()
	{
		// The hook is only ever invoked by ApplyNorseConventions alongside a non-null rewriter (see
		// the fact above) — drive it exactly that way rather than through a null-rewriter path that
		// would never exercise it in production.
		DbContextOptionsBuilder<TemporalTestContext> optionsBuilder = new();
		optionsBuilder.UseSqlServer(ConnectionString);
		optionsBuilder.ApplyNorseConventions(NorseNameRewriters.LowerSnakeCase,
			NorseSqlServerEfProvider.Instance.EntityRenameHook);

		using TemporalTestContext context = new(optionsBuilder.Options);
		var designTimeModel = context.GetService<IDesignTimeModel>().Model;
		var historyTableName =
			designTimeModel.FindEntityType(typeof(TemporalTestEntity))!.GetHistoryTableName();

		historyTableName.ShouldBe("temporal_test_entity_history");
	}

	[Fact]
	void Design_time_placeholder_parses_but_points_at_nothing()
	{
		var placeholder = NorseSqlServerEfProvider.Instance
			.DesignTimePlaceholderConnectionString("norse_identity");

		// IDE0028 false positive: SqlConnectionStringBuilder implements IDictionary, and the
		// collection-expression heuristic (dotnet_style_prefer_collection_expression =
		// when_types_loosely_match) misfires on its single-string constructor overload, suggesting
		// `[placeholder]` — not a valid replacement for parsing a connection string.
#pragma warning disable IDE0028
		SqlConnectionStringBuilder parsed = new(placeholder);
#pragma warning restore IDE0028
		parsed.InitialCatalog.ShouldBe("norse_identity");
		parsed.DataSource.ShouldBe("design");
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options);

	sealed record TemporalTestEntity : INorseEntity<TemporalTestEntity>
	{
		public int Id { get; init; }

		[MaxLength(100)] public string Value { get; init; } = "";

		public static void Configure(EntityTypeBuilder<TemporalTestEntity> builder) { }
	}

	sealed class TemporalTestContext(DbContextOptions<TemporalTestContext> options) : NorseDbContext(options)
	{
		public DbSet<TemporalTestEntity> TemporalTestEntities => Set<TemporalTestEntity>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<TemporalTestEntity>().ToTable(
				"TemporalTestEntities",
				tb => tb.IsTemporal(t => t.UseHistoryTable("TemporalTestEntityHistory")));
		}
	}
}
