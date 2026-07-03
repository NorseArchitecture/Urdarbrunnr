using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.EntityFramework.Tests;

public sealed class NorseDbContextOptionsExtensionsTests
{
	[Fact]
	void ApplyNorseConventions_applies_snake_case_naming()
	{
		var optionsBuilder = new DbContextOptionsBuilder<TestContext>().UseSqlite("Data Source=:memory:");
		NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder);

		using var ctx = new TestContext(optionsBuilder.Options);
		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		tableName.ShouldBe("test_entities");
	}

	[Fact]
	void NorseDbContext_does_not_apply_naming_conventions_on_its_own()
	{
		var options = new DbContextOptionsBuilder<TestContext>()
			.UseSqlite("Data Source=:memory:")
			.Options;

		using var ctx = new TestContext(options);
		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		// EF Core's own default: the DbSet property name, untouched. Naming is now decided
		// exclusively by the provider registration extension used to register a context — see
		// Norse.EntityFramework.PostgreSQL.NorsePostgresContextExtensions.
		tableName.ShouldBe("TestEntities");
	}

	[Fact]
	void NorseDbContext_implements_INorseDbContext()
	{
		var options = new DbContextOptionsBuilder<TestContext>()
			.UseSqlite("Data Source=:memory:")
			.Options;

		using var ctx = new TestContext(options);

		ctx.ShouldBeAssignableTo<INorseDbContext>();
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
}
