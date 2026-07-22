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

	[Fact]
	void ApplyNorseConventions_renames_foreign_key_and_index_names()
	{
		var optionsBuilder = new DbContextOptionsBuilder<RelatedEntitiesContext>().UseSqlite("Data Source=:memory:");
		NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder);

		using var ctx = new RelatedEntitiesContext(optionsBuilder.Options);
		var childEntity = ctx.Model.FindEntityType(typeof(ChildEntity))!;
		var foreignKey = childEntity.GetForeignKeys().Single();
		var index = childEntity.GetIndexes().Single();

		foreignKey.GetConstraintName().ShouldBe("fk_child_entities_parent_entities_parent_entity_id");
		index.GetDatabaseName().ShouldBe("ix_child_entities_parent_entity_id");
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

	sealed class ParentEntity : INorseEntity<ParentEntity>
	{
		public int Id { get; set; }
		public List<ChildEntity> Children { get; set; } = [];

		public static void Configure(EntityTypeBuilder<ParentEntity> builder) { }
	}

	sealed class ChildEntity : INorseEntity<ChildEntity>
	{
		public int Id { get; set; }
		public int ParentEntityId { get; set; }
		public ParentEntity ParentEntity { get; set; } = null!;

		public static void Configure(EntityTypeBuilder<ChildEntity> builder) { }
	}

	sealed class RelatedEntitiesContext(DbContextOptions<RelatedEntitiesContext> options) : NorseDbContext(options)
	{
		public DbSet<ParentEntity> ParentEntities => Set<ParentEntity>();
		public DbSet<ChildEntity> ChildEntities => Set<ChildEntity>();
	}
}
