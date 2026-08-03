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
		optionsBuilder.ApplyNorseConventions(NorseNameRewriters.LowerSnakeCase);
		optionsBuilder.ApplyNorseTrackingBehavior();
		using HistoryTestContext ctx = new(optionsBuilder.Options);
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
	void Applies_UPPER_SNAKE_naming_when_the_upper_rewriter_is_supplied()
	{
		// HistoryTestContext (not RewriteTestContext) is the mirror here: RewriteTestContext hardcodes
		// NorseSnakeCaseNamingConvention with a fixed rewriter via ConfigureConventions, so it can never
		// observe a rewriter supplied through ApplyNorseConventions. HistoryTestContext takes the real,
		// unmodified NorseDbContext.ConfigureConventions and relies solely on the ApplyNorseConventions
		// extension for naming -- the only fixture already in this file that actually exercises a
		// caller-supplied rewrite delegate end to end.
		var optionsBuilder = new DbContextOptionsBuilder<HistoryTestContext>().UseSqlite("Data Source=:memory:");
		optionsBuilder.ApplyNorseConventions(NorseNameRewriters.UpperSnakeCase);
		using HistoryTestContext ctx = new(optionsBuilder.Options);

		var entityType = ctx.Model.FindEntityType(typeof(HistoryTestEntity));
		entityType.ShouldNotBeNull();
		entityType.GetTableName().ShouldBe("HISTORY_TEST_ENTITIES");
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
		await using var ctx = CreateContext<NestedJsonMappedContext>();
		await ctx.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
		await ctx.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
		ctx.Set<NestedJsonMappedOwner>().Add(new()
		{
			Id = 1,
			ShippingDetail = new() { Value = "hello", SubDetail = new() { Value = "world" } },
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
	void Complex_type_mapped_to_JSON_has_its_container_column_name_rewritten()
	{
		// Regression test: ComplexProperty<T>().ToJson() (used by ASP.NET Core Identity's passkey
		// IdentityPasskeyData) is a distinct model construct from OwnsOne(...).ToJson() -- complex types
		// never appear in Model.GetEntityTypes(), so the main convention loop skipped them entirely and
		// left their container column PascalCase (e.g. Postgres emitting a quoted "Data" column instead
		// of data). See NorseSnakeCaseNamingConvention.RenameComplexType.
		using var ctx = CreateContext<ComplexJsonMappedContext>();

		var entityType = ctx.Model.FindEntityType(typeof(ComplexJsonMappedOwner))!;
		var complexProperty = entityType.FindComplexProperty(nameof(ComplexJsonMappedOwner.ShippingDetail))!;

		complexProperty.ComplexType.GetContainerColumnName().ShouldBe("shipping_detail");
	}

	[Fact]
	void Split_table_fragment_name_is_rewritten_to_snake_case()
	{
		// SplitToTable fragments are keyed by StoreObjectIdentifier, not by the entity's table-name
		// annotation, so entity.SetTableName-based renaming never touches them -- a PascalCase authored
		// fragment name used to survive into an otherwise snake_case schema as a quoted identifier.
		using var ctx = CreateContext<SplitTestContext>();

		var entity = ctx.Model.FindEntityType(typeof(SplitTestUser))!;

		var fragment = entity.GetMappingFragments().ShouldHaveSingleItem();
		fragment.StoreObject.Name.ShouldBe("split_test_user_lockout");
	}

	[Fact]
	void Split_fragment_column_membership_survives_the_rename()
	{
		// Fragment membership lives in per-store-object property overrides keyed by the OLD identifier;
		// renaming the fragment without migrating the overrides would silently fold the split columns
		// back into the main table -- the exact silent fallback this platform exists to forbid.
		using var ctx = CreateContext<SplitTestContext>();

		var entity = ctx.Model.FindEntityType(typeof(SplitTestUser))!;
		var lockoutEnd = entity.FindProperty(nameof(SplitTestUser.LockoutEnd))!;
		var mainTable = StoreObjectIdentifier.Table("split_test_users");
		var fragmentTable = StoreObjectIdentifier.Table("split_test_user_lockout");

		lockoutEnd.GetColumnName(fragmentTable).ShouldBe("lockout_end");
		lockoutEnd.GetColumnName(mainTable).ShouldBeNull();
	}

	[Fact]
	void Split_entity_primary_key_names_stay_distinct_per_table()
	{
		// An explicit key-name annotation is GLOBAL per key (RelationalAnnotationNames.Name -- EF has no
		// per-store-object override, verified against EF main), so the convention's usual
		// SetName(rewriteName(...)) would stamp ONE name onto the PK constraint of every fragment table.
		// Postgres backs PK constraints with schema-scoped relations, so the duplicate is a hard 42P07 at
		// migrate time. For fragment-bearing entities the convention must leave key names at EF's
		// per-table defaults -- the PascalCase "PK_" prefix on exactly these constraints is the accepted
		// cost, revisited if EF ever grows per-store-object key naming.
		using var ctx = CreateContext<SplitTestContext>();

		var entity = ctx.Model.FindEntityType(typeof(SplitTestUser))!;
		var primaryKey = entity.FindPrimaryKey()!;

		primaryKey.GetName(StoreObjectIdentifier.Table("split_test_users")).ShouldBe("PK_split_test_users");
		primaryKey.GetName(StoreObjectIdentifier.Table("split_test_user_lockout")).ShouldBe("PK_split_test_user_lockout");
	}

	[Fact]
	void Split_entity_linking_foreign_key_keeps_per_fragment_default_names()
	{
		// EF synthesizes a model-level row-internal FK (self-referencing, PK-to-PK) that maps to one
		// linking constraint PER fragment table. Its no-arg GetConstraintName() default derives from
		// the entity's MAIN table on both sides, so the convention's usual explicit
		// SetConstraintName(rewriteName(...)) stamps that main-table-on-both-sides name globally --
		// misnaming the fragment's constraint (no fragment table in the name) and colliding across
		// fragments on engines with schema-scoped constraint names. EF's per-store defaults name each
		// linking constraint for its own fragment table; the convention must leave them untouched.
		using var ctx = CreateContext<SplitTestContext>();

		var entity = ctx.Model.FindEntityType(typeof(SplitTestUser))!;
		var linkingForeignKey = entity.GetForeignKeys()
			.Single(fk => fk.PrincipalEntityType == entity);

		var constraintName = linkingForeignKey.GetConstraintName(
			StoreObjectIdentifier.Table("split_test_user_lockout"),
			StoreObjectIdentifier.Table("split_test_users"));

		constraintName.ShouldBe("FK_split_test_user_lockout_split_test_users_id");
	}

	[Fact]
	async Task Split_entity_round_trips_through_both_tables()
	{
		// Behavior guard for the fragment re-creation above: if migrating the per-store property
		// overrides ever produced a model EF can't route (columns folded into the wrong table, or a
		// fragment EF no longer recognizes), this surfaces it at SaveChanges/query time -- the
		// model-metadata assertions alone would stay green.
		await using var ctx = CreateContext<SplitTestContext>();
		await ctx.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
		await ctx.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
		ctx.Set<SplitTestUser>().Add(new()
		{
			Id = 1,
			CustomerName = "hello",
			LockoutEnd = new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero),
			AccessFailedCount = 3,
		});
		await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
		ctx.ChangeTracker.Clear();

		var reread = await ctx.Set<SplitTestUser>().SingleAsync(u => u.Id == 1, TestContext.Current.CancellationToken);

		reread.CustomerName.ShouldBe("hello");
		reread.LockoutEnd.ShouldBe(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero));
		reread.AccessFailedCount.ShouldBe(3);
	}

	[Fact]
	void Injected_action_receives_every_entity_and_the_rewrite_function()
	{
		IList<string> invokedEntityClrNames = [];
		Func<string, string>? capturedRewrite = null;

		var optionsBuilder = new DbContextOptionsBuilder<InjectedActionContext>().UseSqlite("Data Source=:memory:");
		optionsBuilder.ApplyNorseTrackingBehavior();
		using InjectedActionContext ctx = new(
			optionsBuilder.Options,
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

	static TContext CreateContext<TContext>() where TContext : DbContext
	{
		var optionsBuilder = new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:");
		optionsBuilder.ApplyNorseTrackingBehavior();
		return (TContext)Activator.CreateInstance(typeof(TContext), optionsBuilder.Options)!;
	}

	sealed record RewriteTestEntity(int Id, string CustomerName = "");

	sealed class RewriteTestContext(DbContextOptions<RewriteTestContext> options) : NorseDbContext(options)
	{
		public DbSet<RewriteTestEntity> RewriteTestEntities =>
			Set<RewriteTestEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new NorseSnakeCaseNamingConvention(NorseNameRewriters.LowerSnakeCase, null));
	}

	// No string properties, unlike RewriteTestEntity -- keeps this out of reach of
	// RequireExplicitLengthConvention, which the real (non-overridden) NorseDbContext.ConfigureConventions
	// activates and RewriteTestContext deliberately bypasses for its own narrower tests.
	sealed record HistoryTestEntity : INorseEntity<HistoryTestEntity>
	{
		public int Id { get; init; }

		public static void Configure(EntityTypeBuilder<HistoryTestEntity> builder)
		{
		}
	}

	sealed class HistoryTestContext(DbContextOptions<HistoryTestContext> options) : NorseDbContext(options)
	{
		public DbSet<HistoryTestEntity> HistoryTestEntities =>
			Set<HistoryTestEntity>();
	}

	sealed record JsonMappedOwner
	{
		public int Id { get; init; }
		public JsonMappedDetail ShippingDetail { get; init; } = new();
	}

	sealed record JsonMappedDetail(string Value = "");

	sealed class JsonMappedContext(DbContextOptions<JsonMappedContext> options) : NorseDbContext(options)
	{
		public DbSet<JsonMappedOwner> JsonMappedOwners =>
			Set<JsonMappedOwner>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new NorseSnakeCaseNamingConvention(NorseNameRewriters.LowerSnakeCase, null));

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<JsonMappedOwner>().OwnsOne(e => e.ShippingDetail, o => o.ToJson());
		}
	}

	sealed record NestedJsonMappedOwner
	{
		public int Id { get; init; }
		public NestedJsonMappedDetail ShippingDetail { get; init; } = new();
	}

	sealed record NestedJsonMappedDetail
	{
		public string Value { get; init; } = "";
		public NestedJsonMappedSubDetail SubDetail { get; init; } = new();
	}

	sealed record NestedJsonMappedSubDetail
	{
		public string Value { get; init; } = "";
	}

	sealed class NestedJsonMappedContext(DbContextOptions<NestedJsonMappedContext> options) : NorseDbContext(options)
	{
		public DbSet<NestedJsonMappedOwner> NestedJsonMappedOwners =>
			Set<NestedJsonMappedOwner>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new NorseSnakeCaseNamingConvention(NorseNameRewriters.LowerSnakeCase, null));

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

	sealed record ComplexJsonMappedOwner
	{
		public int Id { get; init; }
		public ComplexJsonMappedDetail ShippingDetail { get; init; } = new();
	}

	sealed record ComplexJsonMappedDetail
	{
		public string Value { get; init; } = "";
	}

	sealed class ComplexJsonMappedContext(DbContextOptions<ComplexJsonMappedContext> options) : NorseDbContext(options)
	{
		public DbSet<ComplexJsonMappedOwner> ComplexJsonMappedOwners =>
			Set<ComplexJsonMappedOwner>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new NorseSnakeCaseNamingConvention(NorseNameRewriters.LowerSnakeCase, null));

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<ComplexJsonMappedOwner>().ComplexProperty(e => e.ShippingDetail).ToJson();
		}
	}

	sealed record SplitTestUser
	{
		public int Id { get; init; }
		// EF requires at least one non-primary-key property to stay mapped to the main table.
		public string CustomerName { get; init; } = "";
		public DateTimeOffset? LockoutEnd { get; init; }
		public int AccessFailedCount { get; init; }
	}

	sealed class SplitTestContext(DbContextOptions<SplitTestContext> options) : NorseDbContext(options)
	{
		public DbSet<SplitTestUser> SplitTestUsers =>
			Set<SplitTestUser>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new NorseSnakeCaseNamingConvention(NorseNameRewriters.LowerSnakeCase, null));

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<SplitTestUser>(b =>
				b.SplitToTable("SplitTestUserLockout", static lockout =>
				{
					lockout.Property(u => u.LockoutEnd);
					lockout.Property(u => u.AccessFailedCount);
				}));
		}
	}

	sealed class InjectedActionContext(
		DbContextOptions<InjectedActionContext> options,
		Action<IConventionEntityType, Func<string, string>> applyProviderSpecificRenames) : NorseDbContext(options)
	{
		public DbSet<RewriteTestEntity> RewriteTestEntities =>
			Set<RewriteTestEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(_ => new NorseSnakeCaseNamingConvention(NorseNameRewriters.LowerSnakeCase, applyProviderSpecificRenames));
	}
}
