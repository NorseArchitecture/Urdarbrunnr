using Microsoft.EntityFrameworkCore;

namespace Norse.Persistence.EntityFramework.Design.Tests;

public sealed class NorseDesignTimeDbContextFactoryTests
{
	[Fact]
	void Factory_builds_the_context_from_the_bindings_inert_placeholder()
	{
		TestFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		factory.Provider.SeenConnectionString.ShouldBe("Data Source=norse_test.design.db");
	}

	[Fact]
	void Factory_supplies_its_own_assembly_as_the_migrations_assembly()
	{
		TestFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		factory.Provider.SeenMigrationsAssemblyName
			.ShouldBe(typeof(TestFactory).Assembly.GetName().Name);
	}

	[Fact]
	void ConfigureOptions_is_an_override_point_that_can_layer_on_top_of_the_base_wiring()
	{
		OverridingFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		factory.OverrideRan.ShouldBeTrue();
		factory.Provider.SeenConnectionString.ShouldBe("Data Source=norse_test.design.db");
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options);

	abstract class TestFactoryBase : NorseDesignTimeDbContextFactory<TestContext>
	{
		public FakeEfProvider Provider { get; } = new();

		protected override INorseEfProvider ProviderBinding => Provider;

		protected override string DatabaseName => "norse_test";

		protected override TestContext CreateContext(DbContextOptions<TestContext> options) =>
			new(options);
	}

	sealed class TestFactory : TestFactoryBase;

	sealed class OverridingFactory : TestFactoryBase
	{
		public bool OverrideRan { get; private set; }

		protected override void ConfigureOptions(DbContextOptionsBuilder<TestContext> builder)
		{
			base.ConfigureOptions(builder);
			OverrideRan = true;
		}
	}
}
