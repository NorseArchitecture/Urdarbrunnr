using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework.Tests;

public sealed class NorseDbContextOptionsExtensionsTests
{
	[Fact]
	public void ApplyNorseConventions_applies_snake_case_naming()
	{
		var options = new DbContextOptionsBuilder<TestContext>()
			.UseSqlite("Data Source=:memory:")
			.Options;

		using var ctx = new TestContext(options);
		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		tableName.ShouldBe("test_entities");
	}

	[Fact]
	public void NorseDbContext_implements_INorseDbContext()
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

	sealed class TestEntity
	{
		public int Id { get; set; }
		public string Name { get; set; } = "";
	}
}
