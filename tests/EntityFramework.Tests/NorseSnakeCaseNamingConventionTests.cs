using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Norse.EntityFramework.Tests;

public sealed class NorseSnakeCaseNamingConventionTests
{
	[Fact]
	void Table_name_is_rewritten_to_snake_case()
	{
		using var ctx = CreateContext<RewriteTestContext>();

		var tableName = ctx.Model.FindEntityType(typeof(RewriteTestEntity))!.GetTableName();

		tableName.ShouldBe("rewrite_test_entities");
	}

	[Fact]
	void Primary_key_name_is_rewritten_to_snake_case()
	{
		using var ctx = CreateContext<RewriteTestContext>();

		var primaryKey = ctx.Model.FindEntityType(typeof(RewriteTestEntity))!.FindPrimaryKey();

		primaryKey!.GetName().ShouldBe("pk_rewrite_test_entities");
	}

	[Fact]
	void Column_name_is_rewritten_to_snake_case()
	{
		using var ctx = CreateContext<RewriteTestContext>();

		var property = ctx.Model.FindEntityType(typeof(RewriteTestEntity))!
			.FindProperty(nameof(RewriteTestEntity.CustomerName));

		property!.GetColumnName().ShouldBe("customer_name");
	}

	[Fact]
	void Json_mapped_entity_has_only_its_container_column_name_rewritten()
	{
		using var ctx = CreateContext<JsonMappedContext>();

		var jsonEntity = ctx.Model.GetEntityTypes().Single(e => e.IsMappedToJson());

		jsonEntity.GetContainerColumnName().ShouldBe("shipping_detail");
	}

	[Fact]
	void Injected_action_receives_every_entity_and_the_rewrite_function()
	{
		List<string> invokedEntityClrNames = [];
		Func<string, string>? capturedRewrite = null;

		using var ctx = new InjectedActionContext(
			new DbContextOptionsBuilder<InjectedActionContext>().UseSqlite("Data Source=:memory:").Options,
			(entity, rewrite) =>
			{
				invokedEntityClrNames.Add(entity.ClrType.Name);
				capturedRewrite = rewrite;
			});

		_ = ctx.Model;

		invokedEntityClrNames.ShouldContain(nameof(RewriteTestEntity));
		capturedRewrite.ShouldNotBeNull();
		capturedRewrite!("CustomerId").ShouldBe("customer_id");
	}

	static TContext CreateContext<TContext>() where TContext : DbContext =>
		(TContext)Activator.CreateInstance(typeof(TContext),
			new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options)!;

	sealed class RewriteTestEntity
	{
		public int Id { get; set; }
		public string CustomerName { get; set; } = "";
	}

	sealed class RewriteTestContext(DbContextOptions<RewriteTestContext> options) : NorseDbContext(options)
	{
		public DbSet<RewriteTestEntity> RewriteTestEntities => Set<RewriteTestEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new NorseSnakeCaseNamingConvention(null));
	}

	sealed class JsonMappedOwner
	{
		public int Id { get; set; }
		public JsonMappedDetail ShippingDetail { get; set; } = new();
	}

	sealed class JsonMappedDetail
	{
		public string Value { get; set; } = "";
	}

	sealed class JsonMappedContext(DbContextOptions<JsonMappedContext> options) : NorseDbContext(options)
	{
		public DbSet<JsonMappedOwner> JsonMappedOwners => Set<JsonMappedOwner>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new NorseSnakeCaseNamingConvention(null));

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<JsonMappedOwner>().OwnsOne(e => e.ShippingDetail, o => o.ToJson());
		}
	}

	sealed class InjectedActionContext(
		DbContextOptions<InjectedActionContext> options,
		Action<IConventionEntityType, Func<string, string>> applyProviderSpecificRenames) : NorseDbContext(options)
	{
		public DbSet<RewriteTestEntity> RewriteTestEntities => Set<RewriteTestEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(_ => new NorseSnakeCaseNamingConvention(applyProviderSpecificRenames));
	}
}
