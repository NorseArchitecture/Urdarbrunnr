using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Persistence.EntityFramework.Design.Tests;

public sealed class NorseDesignTimeServicesExtensionsTests
{
	[Fact]
	void AddNorseDesignTimeServices_wraps_the_already_registered_scaffolder()
	{
		using var ctx = CreateContext();
		ServiceCollection services = new();
		FakeMigrationsScaffolder efScaffolder = new();
		services.AddSingleton<IMigrationsScaffolder>(efScaffolder);
		services.AddSingleton(CurrentDbContext(ctx));

		services.AddNorseDesignTimeServices("test-db");
		using var provider = services.BuildServiceProvider();

		var scaffolder = provider.GetRequiredService<IMigrationsScaffolder>();

		scaffolder.ShouldBeOfType<DdlEmittingMigrationsScaffolder>();
	}

	[Fact]
	void AddNorseDesignTimeServices_resolved_scaffolder_still_calls_through_to_ef_original()
	{
		using var ctx = CreateContext();
		ServiceCollection services = new();
		FakeMigrationsScaffolder efScaffolder = new();
		services.AddSingleton<IMigrationsScaffolder>(efScaffolder);
		services.AddSingleton(CurrentDbContext(ctx));

		services.AddNorseDesignTimeServices("test-db");
		using var provider = services.BuildServiceProvider();
		var scaffolder = provider.GetRequiredService<IMigrationsScaffolder>();

		scaffolder.ScaffoldMigration("Initial", "MyNamespace");

		efScaffolder.ScaffoldMigrationCallCount.ShouldBe(1);
	}

	[Fact]
	void AddNorseDesignTimeServices_throws_when_ef_has_not_registered_a_scaffolder()
	{
		using var ctx = CreateContext();
		ServiceCollection services = new();
		services.AddSingleton(CurrentDbContext(ctx));

		services.AddNorseDesignTimeServices("test-db");
		using var provider = services.BuildServiceProvider();

		Should.Throw<InvalidOperationException>(provider.GetRequiredService<IMigrationsScaffolder>);
	}

	static StubContext CreateContext() =>
		new(new DbContextOptionsBuilder<StubContext>().UseSqlite("Data Source=:memory:").Options);

	static ICurrentDbContext CurrentDbContext(DbContext ctx) =>
		((IInfrastructure<IServiceProvider>)ctx).Instance.GetRequiredService<ICurrentDbContext>();

	sealed class StubContext(DbContextOptions<StubContext> options) : NorseDbContext(options)
	{
		public DbSet<StubEntity> StubEntities => Set<StubEntity>();
	}

	sealed class StubEntity : INorseEntity<StubEntity>
	{
		public int Id { get; set; }

		public static void Configure(EntityTypeBuilder<StubEntity> builder)
		{
		}
	}
}
