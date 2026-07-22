using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.EntityFramework.SqlServer.Tests;

public sealed class NorseSqlServerContextExtensionsTests
{
	const string ConnectionString = "Server=localhost;Database=test;Trusted_Connection=True;TrustServerCertificate=True;";

	[Fact]
	void AddNorseSqlServerContext_registers_TContext_in_DI()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });

		builder.AddNorseSqlServerContext<TestContext>("test-db");

		var descriptor = builder.Services
			.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
	}

	[Fact]
	void AddNorseSqlServerMigrationContext_registers_TContext_non_pooled_in_DI()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });

		builder.AddNorseSqlServerMigrationContext<TestContext>("test-db", "Norse.EntityFramework.SqlServer.Tests");

		var descriptor = builder.Services
			.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();

		// AddDbContext registers TContext as a direct type-to-type mapping (ImplementationType set,
		// no factory). AddDbContextPool instead registers TContext via a factory that leases an
		// instance from an internal pool (ImplementationFactory set, ImplementationType null) --
		// this distinguishes non-pooled registration from pooled registration.
		descriptor.ImplementationType.ShouldBe(typeof(TestContext));
		descriptor.ImplementationFactory.ShouldBeNull();
	}

	[Fact]
	void AddNorseSqlServerMigrationContext_does_not_throw_with_mutating_OnConfiguring()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });

		builder.AddNorseSqlServerMigrationContext<TestContext>("test-db", "Norse.EntityFramework.SqlServer.Tests");

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();

		Should.NotThrow(() => _ = ctx.Model);
	}

	[Fact]
	void AddNorseSqlServerMigrationContext_defaults_to_native_PascalCase_naming()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });

		builder.AddNorseSqlServerMigrationContext<TestContext>("test-db", "Norse.EntityFramework.SqlServer.Tests");

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();

		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		tableName.ShouldBe("TestEntities");
	}

	[Fact]
	void AddNorseSqlServerMigrationContext_opts_into_snake_case_naming_when_requested()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });

		builder.AddNorseSqlServerMigrationContext<TestContext>(
			"test-db", "Norse.EntityFramework.SqlServer.Tests", useSnakeCaseNaming: true);

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();

		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		tableName.ShouldBe("test_entities");
	}

	[Fact]
	void AddNorseSqlServerMigrationContext_renames_temporal_history_table_when_snake_case_requested()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });

		builder.AddNorseSqlServerMigrationContext<TemporalTestContext>(
			"test-db", "Norse.EntityFramework.SqlServer.Tests", useSnakeCaseNaming: true);

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TemporalTestContext>();

		var designTimeModel = ctx.GetService<IDesignTimeModel>().Model;
		var historyTableName = designTimeModel.FindEntityType(typeof(TemporalTestEntity))!.GetHistoryTableName();

		historyTableName.ShouldBe("temporal_test_entity_history");
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options)
	{
		public DbSet<TestEntity> TestEntities => Set<TestEntity>();
	}

	sealed class TestEntity : INorseEntity<TestEntity>
	{
		public int Id { get; set; }

		[MaxLength(100)]
		public string Name { get; set; } = "";

		public static void Configure(EntityTypeBuilder<TestEntity> builder) { }
	}

	sealed class TemporalTestEntity : INorseEntity<TemporalTestEntity>
	{
		public int Id { get; set; }

		[MaxLength(100)]
		public string Value { get; set; } = "";

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
