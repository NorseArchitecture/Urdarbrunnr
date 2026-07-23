using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class NorseSnakeCaseNamingConventionTests
{
	[Fact]
	void Migrations_history_table_name_is_not_rewritten()
	{
		// HistoryRepository.TableName (used verbatim for raw SQL like "LOCK TABLE") is sourced from
		// RelationalOptionsExtension.MigrationsHistoryTableName, never from the model-conventions
		// pipeline this convention runs in. If this convention renamed HistoryRow's table anyway, the
		// model-driven CREATE script would target a different table than TableName-based raw SQL uses,
		// e.g. Npgsql's AcquireDatabaseLockAsync issuing "LOCK TABLE __EFMigrationsHistory" against a
		// table that was actually created as "__ef_migrations_history" -- a live 42P01 bug this test
		// exists to pin down. Must go through ApplyNorseConventions (the real IConventionSetPlugin DI
		// registration production actually uses), not ConfigureConventions -- HistoryRepository builds
		// its own separate model via the DI-resolved convention set, which per-context
		// ConfigureConventions additions never reach.
		var optionsBuilder = new DbContextOptionsBuilder<HistoryTestContext>().UseSqlite("Data Source=:memory:");
		NorseDbContextOptionsExtensions.ApplyNorseConventions(optionsBuilder);
		using var ctx = new HistoryTestContext(optionsBuilder.Options);
		var historyRepository = ctx.GetService<IHistoryRepository>();

		var createScript = historyRepository.GetCreateScript();

		createScript.ShouldContain(HistoryRepository.DefaultTableName);
	}

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
	void Nested_json_entity_shares_the_root_entitys_container_column_unrewritten_again()
	{
		// The nested entity (SubDetails, owned by JsonMappedDetail which is itself JSON-mapped) must
		// NOT get its own independent rename pass -- it shares the root's already-renamed container.
		// Renaming it a second time is what corrupts EF Core 11 preview6's JSON shaper; see
		// NorseSnakeCaseNamingConvention's remarks.
		using var ctx = CreateContext<NestedJsonMappedContext>();

		var rootEntity = ctx.Model.FindEntityType(typeof(NestedJsonMappedDetail))!;
		var nestedEntity = ctx.Model.FindEntityType(typeof(NestedJsonMappedSubDetail))!;

		rootEntity.GetContainerColumnName().ShouldBe("shipping_detail");
		nestedEntity.GetContainerColumnName().ShouldBe(rootEntity.GetContainerColumnName());
	}

	[Fact]
	async Task Nested_json_mapped_entity_round_trips_through_an_actual_query()
	{
		// Regression guard for the EF Core 11 preview6 JSON shaper crash NorseSnakeCaseNamingConvention's
		// remarks describe -- a model-metadata-only assertion (as above) would not have caught it, since
		// the crash only surfaces compiling an actual query's shaper, not during model building.
		using var ctx = CreateContext<NestedJsonMappedContext>();
		await ctx.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
		await ctx.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
		ctx.Set<NestedJsonMappedOwner>().Add(new NestedJsonMappedOwner
		{
			Id = 1,
			ShippingDetail = new NestedJsonMappedDetail { Value = "hello", SubDetail = new NestedJsonMappedSubDetail { Value = "world" } },
		});
		await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
		ctx.ChangeTracker.Clear();

		var reread = await ctx.Set<NestedJsonMappedOwner>().SingleAsync(o => o.Id == 1, TestContext.Current.CancellationToken);

		reread.ShippingDetail.ShouldNotBeNull();
		reread.ShippingDetail.Value.ShouldBe("hello");
		reread.ShippingDetail.SubDetail.ShouldNotBeNull();
		reread.ShippingDetail.SubDetail.Value.ShouldBe("world");
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

	// No string properties, unlike RewriteTestEntity -- keeps this out of reach of
	// RequireExplicitLengthConvention, which the real (non-overridden) NorseDbContext.ConfigureConventions
	// activates and RewriteTestContext deliberately bypasses for its own narrower tests.
	sealed class HistoryTestEntity : INorseEntity<HistoryTestEntity>
	{
		public int Id { get; set; }

		public static void Configure(EntityTypeBuilder<HistoryTestEntity> builder)
		{
		}
	}

	sealed class HistoryTestContext(DbContextOptions<HistoryTestContext> options) : NorseDbContext(options)
	{
		public DbSet<HistoryTestEntity> HistoryTestEntities => Set<HistoryTestEntity>();
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

	sealed class NestedJsonMappedOwner
	{
		public int Id { get; set; }
		public NestedJsonMappedDetail ShippingDetail { get; set; } = new();
	}

	sealed class NestedJsonMappedDetail
	{
		public string Value { get; set; } = "";
		public NestedJsonMappedSubDetail SubDetail { get; set; } = new();
	}

	sealed class NestedJsonMappedSubDetail
	{
		public string Value { get; set; } = "";
	}

	sealed class NestedJsonMappedContext(DbContextOptions<NestedJsonMappedContext> options) : NorseDbContext(options)
	{
		public DbSet<NestedJsonMappedOwner> NestedJsonMappedOwners => Set<NestedJsonMappedOwner>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new NorseSnakeCaseNamingConvention(null));

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<NestedJsonMappedOwner>().OwnsOne(e => e.ShippingDetail, o =>
			{
				o.ToJson();
				o.OwnsOne(d => d.SubDetail);
			});
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
