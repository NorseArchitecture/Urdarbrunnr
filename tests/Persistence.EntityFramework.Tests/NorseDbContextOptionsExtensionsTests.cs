using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class NorseDbContextOptionsExtensionsTests
{
	[Fact]
	void ApplyNorseConventions_applies_snake_case_naming()
	{
		var optionsBuilder = new DbContextOptionsBuilder<TestContext>().UseSqlite("Data Source=:memory:");
		optionsBuilder.ApplyNorseConventions(NorseNameRewriters.LowerSnakeCase);

		using TestContext ctx = new(optionsBuilder.Options);
		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		tableName.ShouldBe("test_entities");
	}

	[Fact]
	void NorseDbContext_does_not_apply_naming_conventions_on_its_own()
	{
		var options = new DbContextOptionsBuilder<TestContext>()
			.UseSqlite("Data Source=:memory:")
			.Options;

		using TestContext ctx = new(options);
		var tableName = ctx.Model.FindEntityType(typeof(TestEntity))!.GetTableName();

		// EF Core's own default: the DbSet property name, untouched. Naming is now binding data,
		// supplied by the registering provider's INorseEfProvider.NameRewriter and wired in by
		// ApplyNorseProviderOptions — never applied by NorseDbContext on its own.
		tableName.ShouldBe("TestEntities");
	}

	[Fact]
	void NorseDbContext_implements_INorseDbContext()
	{
		var options = new DbContextOptionsBuilder<TestContext>()
			.UseSqlite("Data Source=:memory:")
			.Options;

		using TestContext ctx = new(options);

		ctx.ShouldBeAssignableTo<INorseDbContext>();
	}

	[Fact]
	void ApplyNorseConventions_renames_foreign_key_and_index_names()
	{
		var optionsBuilder = new DbContextOptionsBuilder<RelatedEntitiesContext>().UseSqlite("Data Source=:memory:");
		optionsBuilder.ApplyNorseConventions(NorseNameRewriters.LowerSnakeCase);

		using RelatedEntitiesContext ctx = new(optionsBuilder.Options);
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

	sealed record TestEntity : INorseEntity<TestEntity>
	{
		public int Id { get; init; }

		[MaxLength(100)] public string Name { get; init; } = "";

		public static void Configure(EntityTypeBuilder<TestEntity> builder)
		{
		}
	}

	sealed record ParentEntity : INorseEntity<ParentEntity>
	{
		public int Id { get; init; }
		public ICollection<ChildEntity> Children { get; init; } = [];

		public static void Configure(EntityTypeBuilder<ParentEntity> builder)
		{
		}
	}

	sealed record ChildEntity : INorseEntity<ChildEntity>
	{
		public int Id { get; init; }
		public int ParentEntityId { get; init; }
		public ParentEntity ParentEntity { get; init; } = null!;

		public static void Configure(EntityTypeBuilder<ChildEntity> builder)
		{
		}
	}

	sealed class RelatedEntitiesContext(DbContextOptions<RelatedEntitiesContext> options)
		: NorseDbContext(options)
	{
		public DbSet<ParentEntity> ParentEntities => Set<ParentEntity>();
		public DbSet<ChildEntity> ChildEntities => Set<ChildEntity>();
	}
}
