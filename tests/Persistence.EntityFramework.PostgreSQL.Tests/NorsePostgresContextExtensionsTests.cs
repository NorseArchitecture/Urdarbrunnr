using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

public sealed class NorsePostgresContextExtensionsTests
{
	[Fact]
	void AddNorsePostgresContext_registers_TContext_in_DI()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = "Host=localhost;Database=test" });

		builder.AddNorsePostgresContext<TestContext>("test-db");

		var descriptor = builder.Services
			.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
	}

	[Fact]
	void AddNorsePostgresMigrationContext_registers_TContext_non_pooled_in_DI()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = "Host=localhost;Database=test" });

		builder.AddNorsePostgresMigrationContext<TestContext>("test-db", "Norse.Persistence.EntityFramework.PostgreSQL.Tests");

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
	void AddNorsePostgresMigrationContext_does_not_throw_with_mutating_OnConfiguring()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = "Host=localhost;Database=test" });

		builder.AddNorsePostgresMigrationContext<TestContext>("test-db", "Norse.Persistence.EntityFramework.PostgreSQL.Tests");

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();

		Should.NotThrow(() => _ = ctx.Model);
	}

	[Fact]
	void AddNorsePostgresMigrationContext_defaults_to_snake_case_naming()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = "Host=localhost;Database=test" });

		builder.AddNorsePostgresMigrationContext<TestContext>("test-db", "Norse.Persistence.EntityFramework.PostgreSQL.Tests");

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();

		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		tableName.ShouldBe("test_entities");
	}

	[Fact]
	void AddNorsePostgresMigrationContext_opts_out_of_snake_case_naming_when_requested()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = "Host=localhost;Database=test" });

		builder.AddNorsePostgresMigrationContext<TestContext>(
			"test-db", "Norse.Persistence.EntityFramework.PostgreSQL.Tests", useSnakeCaseNaming: false);

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();

		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		tableName.ShouldBe("TestEntities");
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options)
	{
		public DbSet<TestEntity> TestEntities => Set<TestEntity>();
	}

	sealed record TestEntity : INorseEntity<TestEntity>
	{
		public int Id { get; init; }

		[MaxLength(100)]
		public string Name { get; init; } = "";

		public static void Configure(EntityTypeBuilder<TestEntity> builder) { }
	}
}
