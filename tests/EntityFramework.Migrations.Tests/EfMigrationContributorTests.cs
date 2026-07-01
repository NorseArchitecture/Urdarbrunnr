using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework.Migrations.Tests;

public sealed class EfMigrationContributorTests
{
	[Fact]
	void MigrationConnectionStringAttribute_stores_name()
	{
		MigrationConnectionStringAttribute attr = new("my-db");

		attr.ConnectionStringName.ShouldBe("my-db");
	}

	[Fact]
	void Name_returns_subclass_value()
	{
		using var ctx = CreateContext();
		StubContributor sut = new(ctx);

		sut.Name.ShouldBe("Stub");
	}

	static StubContext CreateContext() =>
		new(new DbContextOptionsBuilder<StubContext>()
			.UseInMemoryDatabase("test-ef-migrations")
			.Options);

	[MigrationConnectionString("stub-db")]
	sealed class StubContributor(StubContext context) : EfMigrationContributor<StubContext>(context)
	{
		public override string Name => "Stub";
	}

	sealed class StubContext(DbContextOptions<StubContext> options) : NorseDbContext(options);
}
