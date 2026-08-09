using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

/// <summary>
///     DDL snapshot coverage for the create path of the PostgreSQL temporal apparatus (spec §3.1–§3.2).
///     No database: <c>Database.GenerateCreateScript()</c> runs the real create operations through the
///     real generator, which is exactly the path the DBA schema dump takes (spec §3.5).
/// </summary>
public sealed class TemporalCreateTableSqlTests
{
	static string Script<TEntity>() where TEntity : class
	{
		using var context = PostgresTestContext.Create<TEntity>();
		return context.Database.GenerateCreateScript();
	}

	[Fact]
	void Emits_the_pg19_floor_assert() =>
		Script<TemporalWidget>().ShouldContain("server_version_num");

	[Fact]
	void Emits_the_btree_gist_guard_with_the_provisioning_diagnostic()
	{
		var script = Script<TemporalWidget>();

		script.ShouldContain("btree_gist");
		script.ShouldContain("insufficient_privilege");
		script.ShouldContain("provisioning prerequisite");
	}

	[Fact]
	void Adds_the_db_owned_system_period_column_with_no_default()
	{
		// No column DEFAULT: the BEFORE INSERT trigger assigns the period (§3.2 amendment, 2026-08-05) —
		// a default cannot be told apart from a client-supplied value once applied.
		var script = Script<TemporalWidget>();

		script.ShouldContain("""ALTER TABLE "public"."temporal_widget" ADD COLUMN system_period tstzrange NOT NULL;""");
		script.ShouldNotContain("now()");
	}

	[Fact]
	void The_insert_branch_assigns_the_clock_timestamp_period_and_rejects_a_client_supplied_one()
	{
		var script = Script<TemporalWidget>();

		script.ShouldContain("IF TG_OP = 'INSERT' THEN");
		script.ShouldContain("NEW.system_period := pg_catalog.tstzrange(pg_catalog.clock_timestamp(), 'infinity');");
	}

	[Fact]
	void Creates_the_history_table_with_the_without_overlaps_primary_key()
	{
		var script = Script<TemporalWidget>();

		script.ShouldContain("CREATE TABLE \"public\".\"temporal_widget_history\"");
		script.ShouldContain("PRIMARY KEY (\"id\", \"system_period\" WITHOUT OVERLAPS)");
	}

	[Fact]
	void Mirrors_history_columns_by_name_and_store_type_with_only_the_key_not_null()
	{
		var script = Script<TemporalWidget>();

		// Projection rule (spec §3.4): name + store type only; nullable except the PK components.
		script.ShouldContain("\"id\" integer NOT NULL,");
		script.ShouldContain("\"name\" character varying(100),");
	}

	[Fact]
	void Creates_the_hardened_security_definer_trigger_function()
	{
		var script = Script<TemporalWidget>();

		script.ShouldContain("SECURITY DEFINER");
		script.ShouldContain("SET search_path = pg_catalog");
		script.ShouldContain("REVOKE EXECUTE");
		script.ShouldContain("greatest(pg_catalog.clock_timestamp()");
	}

	[Fact]
	void Creates_the_insert_update_and_delete_triggers_on_the_main_table()
	{
		var script = Script<TemporalWidget>();

		script.ShouldContain(
			"CREATE TRIGGER \"temporal_widget_versioning_insert\" BEFORE INSERT ON \"public\".\"temporal_widget\"");
		script.ShouldContain(
			"CREATE TRIGGER \"temporal_widget_versioning_update\" BEFORE UPDATE ON \"public\".\"temporal_widget\"");
		script.ShouldContain(
			"CREATE TRIGGER \"temporal_widget_versioning_delete\" BEFORE DELETE ON \"public\".\"temporal_widget\"");
	}

	[Fact]
	void Creates_the_timeline_view() =>
		Script<TemporalWidget>().ShouldContain("_timeline");

	[Fact]
	void Emits_the_prelude_before_the_temporal_table_create()
	{
		// In a non-transactional script workflow (GenerateCreateScript, psql without a wrapping
		// transaction), the floor/schema asserts have to run before the unqualified main table lands —
		// otherwise a wrong search_path can create the table in the wrong schema before the assert fires.
		var script = Script<TemporalWidget>();

		var floorAssert = Position(script, "current_setting('server_version_num')::int");
		var createTable = Position(script, "CREATE TABLE temporal_widget (");

		floorAssert.ShouldBeLessThan(createTable);
	}

	[Fact]
	void Emits_the_floor_assert_and_extension_guard_once_per_migration()
	{
		using TwoTemporalWidgetContext context = new(
			PostgresTestContext.Options<TwoTemporalWidgetContext>());

		var script = context.Database.GenerateCreateScript();

		Occurrences(script, "CREATE FUNCTION").ShouldBe(2, "both tables get their own versioning function");
		Occurrences(script, "current_setting('server_version_num')::int").ShouldBe(1);
		Occurrences(script, "CREATE EXTENSION btree_gist").ShouldBe(1);
	}

	[Fact]
	void Emits_the_prelude_again_for_the_next_migration_off_the_same_generator()
	{
		// The once-per-migration flag lives on the generator, which EF resolves per context scope —
		// a second batch through the same instance must still get its floor assert.
		using var context = PostgresTestContext.Create<TemporalWidget>();

		context.Database.GenerateCreateScript();
		var second = context.Database.GenerateCreateScript();

		Occurrences(second, "current_setting('server_version_num')::int").ShouldBe(1);
	}

	[Fact]
	void Asserts_the_session_default_schema_when_no_schema_was_declared()
	{
		// Npgsql emits the main table unqualified, so it lands wherever the search path points, while
		// the apparatus has to be qualified for the SECURITY DEFINER function. The migration asserts
		// the two agree instead of quietly building the apparatus in the wrong schema.
		var script = Script<TemporalWidget>();

		script.ShouldContain("pg_catalog.current_schema() <> 'public'");
		script.ShouldContain("CREATE TABLE \"public\".\"temporal_widget_history\"");
	}

	[Fact]
	void Skips_the_default_schema_assert_when_the_model_declares_a_schema()
	{
		using DeclaredSchemaContext context = new(PostgresTestContext.Options<DeclaredSchemaContext>());

		var script = context.Database.GenerateCreateScript();

		script.ShouldNotContain("current_schema()");
		script.ShouldContain("CREATE TABLE \"norse_audit\".\"temporal_widget_history\"");
	}

	[Fact]
	void A_split_fragment_table_gets_no_apparatus()
	{
		var script = Script<SplitTemporalWidget>();

		script.ShouldContain("split_temporal_widgets_history");
		script.ShouldNotContain("widget_counters_history");
		script.ShouldNotContain("ON \"public\".\"widget_counters\"");
	}

	[Fact]
	void The_annotation_provider_marks_the_main_table_and_not_the_split_fragment()
	{
		using var context = PostgresTestContext.Create<SplitTemporalWidget>();
		var relationalModel = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();

		Table(relationalModel, "split_temporal_widgets")
			.FindAnnotation(NorseAnnotationNames.Temporal)
			.ShouldNotBeNull()
			.Value.ShouldBe(true);
		Table(relationalModel, "widget_counters")
			.FindAnnotation(NorseAnnotationNames.Temporal)
			.ShouldBeNull();
	}

	[Fact]
	void An_unmarked_entity_gets_no_apparatus() =>
		Script<PlainWidget>().ShouldNotContain("system_period");

	static int Occurrences(string script, string value) =>
		script.Split(value).Length - 1;

	static int Position(string script, string statement)
	{
		var position = script.IndexOf(statement, StringComparison.Ordinal);
		position.ShouldBeGreaterThanOrEqualTo(0, $"'{statement}' should have been emitted");
		return position;
	}

	static ITable Table(IRelationalModel relationalModel, string name) =>
		relationalModel.Tables.Single(table => table.Name == name);

	static class PostgresTestContext
	{
		public static DbContextOptions<TContext> Options<TContext>() where TContext : DbContext
		{
			DbContextOptionsBuilder<TContext> optionsBuilder = new();
			optionsBuilder.ApplyNorseProviderOptions(NorsePostgresEfProvider.Instance,
				NorsePostgresEfProvider.Instance.DesignTimePlaceholderConnectionString("norse_test"),
				migrationsAssemblyName: null);
			return optionsBuilder.Options;
		}

		public static TemporalTestContext<TEntity> Create<TEntity>() where TEntity : class =>
			new(Options<TemporalTestContext<TEntity>>());
	}

	sealed class TemporalTestContext<TEntity>(DbContextOptions<TemporalTestContext<TEntity>> options)
		: NorseDbContext(options)
		where TEntity : class
	{
		public DbSet<TEntity> Entities => Set<TEntity>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			if (typeof(TEntity) == typeof(TemporalWidget))
				builder.Entity<TemporalWidget>().ToTable("temporal_widget");
			else if (typeof(TEntity) == typeof(PlainWidget))
				builder.Entity<PlainWidget>().ToTable("plain_widget");
			else if (typeof(TEntity) == typeof(SplitTemporalWidget))
			{
				builder.Entity<SplitTemporalWidget>()
					.ToTable("split_temporal_widgets")
					.SplitToTable("widget_counters",
						static counters => counters.Property(widget => widget.AccessCount));
			}
		}
	}

	sealed class TwoTemporalWidgetContext(DbContextOptions<TwoTemporalWidgetContext> options)
		: NorseDbContext(options)
	{
		public DbSet<TemporalWidget> Widgets => Set<TemporalWidget>();

		public DbSet<TemporalGadget> Gadgets => Set<TemporalGadget>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<TemporalWidget>().ToTable("temporal_widget");
			builder.Entity<TemporalGadget>().ToTable("temporal_gadget");
		}
	}

	sealed class DeclaredSchemaContext(DbContextOptions<DeclaredSchemaContext> options)
		: NorseDbContext(options)
	{
		public DbSet<TemporalWidget> Widgets => Set<TemporalWidget>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.HasDefaultSchema("norse_audit");
			builder.Entity<TemporalWidget>().ToTable("temporal_widget");
		}
	}

	sealed record TemporalWidget : ITemporalEntity, INorseEntity<TemporalWidget>
	{
		public int Id { get; init; }

		[MaxLength(100)] public string Name { get; init; } = "";

		public static void Configure(EntityTypeBuilder<TemporalWidget> builder)
		{
		}
	}

	sealed record TemporalGadget : ITemporalEntity, INorseEntity<TemporalGadget>
	{
		public int Id { get; init; }

		[MaxLength(100)] public string Name { get; init; } = "";

		public static void Configure(EntityTypeBuilder<TemporalGadget> builder)
		{
		}
	}

	// SplitTemporalWidget is not nested here: the integration suite drives the same model against a real
	// server, so it lives beside the other shared models in TemporalEvolutionModels.cs.

	sealed record PlainWidget : INorseEntity<PlainWidget>
	{
		public int Id { get; init; }

		[MaxLength(100)] public string Name { get; init; } = "";

		public static void Configure(EntityTypeBuilder<PlainWidget> builder)
		{
		}
	}
}
