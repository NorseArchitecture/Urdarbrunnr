using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework.Design.PostgreSQL.Tests;

public sealed class NorsePostgreSqlDesignTimeDbContextFactoryTests
{
	[Fact]
	void CreateDbContext_wires_the_npgsql_provider()
	{
		StubFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		ctx.Database.ProviderName.ShouldBe("Npgsql.EntityFrameworkCore.PostgreSQL");
	}

	[Fact]
	void CreateDbContext_defaults_to_snake_case_naming()
	{
		StubFactory factory = new();

		using var ctx = factory.CreateDbContext([]);

		var tableName = ctx.Model.FindEntityType(typeof(StubEntity))!.GetTableName();

		tableName.ShouldBe("stub_entities");
	}

	[Fact]
	void CreateDbContext_uses_the_environment_connection_string_override_when_set()
	{
		Environment.SetEnvironmentVariable("DOTNET_EFTOOLS_CONNECTIONSTRING", "Host=override-host;Database=override");
		try
		{
			StubFactory factory = new();

			using var ctx = factory.CreateDbContext([]);

			ctx.Database.GetConnectionString().ShouldBe("Host=override-host;Database=override");
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

		ctx.Database.ProviderName.ShouldBe("Npgsql.EntityFrameworkCore.PostgreSQL");
		factory.ExtraConfigurationRan.ShouldBeTrue();
	}

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

	sealed class StubFactory : NorsePostgreSqlDesignTimeDbContextFactory<StubContext>
	{
		protected override string DatabaseName => "stub_db";

		protected override StubContext CreateContext(DbContextOptions<StubContext> options) => new(options);
	}

	sealed class OverridingStubFactory : NorsePostgreSqlDesignTimeDbContextFactory<StubContext>
	{
		public bool ExtraConfigurationRan { get; private set; }

		protected override string DatabaseName => "stub_db";

		protected override void ConfigureOptions(DbContextOptionsBuilder<StubContext> builder, string connectionString)
		{
			base.ConfigureOptions(builder, connectionString);
			ExtraConfigurationRan = true;
		}

		protected override StubContext CreateContext(DbContextOptions<StubContext> options) => new(options);
	}
}
