using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework.Design.SqlServer.Tests;

public sealed class NorseSqlServerDesignTimeDbContextFactoryTests
{
	[Fact]
	void CreateDbContext_wires_the_sql_server_provider()
	{
		StubFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		ctx.Database.ProviderName.ShouldBe("Microsoft.EntityFrameworkCore.SqlServer");
	}

	[Fact]
	void CreateDbContext_defaults_to_pascal_case_naming()
	{
		StubFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		var tableName = ctx.Model.FindEntityType(typeof(StubEntity))!.GetTableName();

		tableName.ShouldBe("StubEntities");
	}

	[Fact]
	void CreateDbContext_uses_the_environment_connection_string_override_when_set()
	{
		Environment.SetEnvironmentVariable("DOTNET_EFTOOLS_CONNECTIONSTRING", "Server=override-host;Database=override");
		try
		{
			StubFactory factory = new();

			using var ctx = factory.CreateDbContext([]);

			var connString = ctx.Database.GetConnectionString();
			connString.ShouldNotBeNull();
			connString.ShouldContain("override-host");
			connString.ShouldContain("override");
		}
		finally
		{
			Environment.SetEnvironmentVariable("DOTNET_EFTOOLS_CONNECTIONSTRING", null);
		}
	}

	[Fact]
	void A_subclass_overriding_ConfigureOptions_composes_with_the_base_wiring_instead_of_replacing_it()
	{
		OverridingStubFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		ctx.Database.ProviderName.ShouldBe("Microsoft.EntityFrameworkCore.SqlServer");
		factory.ExtraConfigurationRan.ShouldBeTrue();
	}

	sealed class StubContext(DbContextOptions<StubContext> options) : NorseDbContext(options)
	{
		public DbSet<StubEntity> StubEntities => Set<StubEntity>();
	}

	sealed record StubEntity : INorseEntity<StubEntity>
	{
		public int Id { get; init; }

		public static void Configure(EntityTypeBuilder<StubEntity> builder)
		{
		}
	}

	sealed class StubFactory : NorseSqlServerDesignTimeDbContextFactory<StubContext>
	{
		protected override string DatabaseName => "stub_db";

		protected override StubContext CreateContext(DbContextOptions<StubContext> options) =>
			new(options);
	}

	sealed class OverridingStubFactory : NorseSqlServerDesignTimeDbContextFactory<StubContext>
	{
		public bool ExtraConfigurationRan { get; private set; }

		protected override string DatabaseName => "stub_db";

		protected override void ConfigureOptions(DbContextOptionsBuilder<StubContext> builder, string connectionString)
		{
			base.ConfigureOptions(builder, connectionString);
			ExtraConfigurationRan = true;
		}

		protected override StubContext CreateContext(DbContextOptions<StubContext> options) =>
			new(options);
	}
}
