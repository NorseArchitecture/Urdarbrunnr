using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

/// <summary>
///     DDL coverage for the enable/disable transitions of the PostgreSQL temporal apparatus
///     (spec §3.3). No database and no hand-built operations: EF's real model differ produces the
///     <see cref="Microsoft.EntityFrameworkCore.Migrations.Operations.AlterTableOperation" /> from two
///     model variants of the same table, and the real generator turns it into SQL — so the diffing and
///     the emission are both under test in one arrange.
/// </summary>
public sealed class TemporalTransitionSqlTests
{
	// Two entities, one table name, identical column shape: the only thing the differ can see between
	// the two models is the temporal marker appearing or disappearing.
	const string TransitionTable = "transition_widget";

	const string DeclaredSchema = "norse_audit";

	[Fact]
	void Enabling_on_an_existing_table_backfills_with_a_single_captured_timestamp()
	{
		var sql = EnableSql();

		sql.ShouldContain("ADD COLUMN system_period tstzrange");
		sql.ShouldContain("ts := pg_catalog.clock_timestamp()");
		sql.ShouldContain("WHERE system_period IS NULL");
		sql.ShouldContain("SET NOT NULL");
		sql.ShouldContain("PRIMARY KEY (\"id\", \"system_period\" WITHOUT OVERLAPS)");
	}

	[Fact]
	void Enabling_adds_the_column_nullable_before_the_backfill_stamps_it()
	{
		// The column arrives without NOT NULL and without a default: a volatile per-row default would
		// scatter each pre-existing row's open bound across the table rewrite (spec §3.3).
		var sql = EnableSql();

		sql.ShouldContain("ADD COLUMN system_period tstzrange;");
		sql.IndexOf("ADD COLUMN system_period", StringComparison.Ordinal)
			.ShouldBeLessThan(sql.IndexOf("ts := pg_catalog.clock_timestamp()", StringComparison.Ordinal));
		sql.IndexOf("ts := pg_catalog.clock_timestamp()", StringComparison.Ordinal)
			.ShouldBeLessThan(sql.IndexOf("SET NOT NULL", StringComparison.Ordinal));
	}

	[Fact]
	void Enabling_stamps_every_pre_existing_row_from_one_captured_timestamp()
	{
		var sql = EnableSql();

		// One capture, one UPDATE — never clock_timestamp() read per row.
		Occurrences(sql, "clock_timestamp()").ShouldBe(3,
			"one capture, one INSERT-branch assignment, one closure clamp");
		Occurrences(sql, "UPDATE \"public\".\"transition_widget\" SET system_period").ShouldBe(1);
		sql.ShouldContain("SET system_period = pg_catalog.tstzrange(ts, 'infinity')");
		sql.ShouldNotContain("now()");
	}

	[Fact]
	void Enabling_restores_no_column_default_the_insert_trigger_assigns_the_period()
	{
		// A column default cannot be told apart from a client-supplied value once applied (§3.2
		// amendment, 2026-08-05): the BEFORE INSERT trigger — created moments later in this same
		// transition — assigns the period for every row inserted from here on.
		var sql = EnableSql();

		sql.ShouldNotContain("SET DEFAULT");
		sql.ShouldContain("CREATE TRIGGER \"transition_widget_versioning_insert\" BEFORE INSERT");
	}

	[Fact]
	void Enabling_emits_the_floor_assert_and_extension_guard()
	{
		var sql = EnableSql();

		sql.ShouldContain("server_version_num");
		sql.ShouldContain("CREATE EXTENSION btree_gist");
		sql.ShouldContain("insufficient_privilege");
	}

	[Fact]
	void Enabling_builds_the_rest_of_the_apparatus_exactly_like_the_create_path()
	{
		var sql = EnableSql();

		sql.ShouldContain("CREATE TABLE \"public\".\"transition_widget_history\"");
		sql.ShouldContain("CREATE FUNCTION \"public\".\"transition_widget_versioning\"()");
		sql.ShouldContain("CREATE TRIGGER \"transition_widget_versioning_insert\"");
		sql.ShouldContain("CREATE TRIGGER \"transition_widget_versioning_update\"");
		sql.ShouldContain("CREATE TRIGGER \"transition_widget_versioning_delete\"");
		sql.ShouldContain("CREATE VIEW \"public\".\"transition_widget_timeline\"");
	}

	[Fact]
	void Disabling_drops_apparatus_then_column_as_explicit_statements()
	{
		var sql = DisableSql();

		sql.ShouldContain("DROP TRIGGER");
		sql.ShouldContain("DROP FUNCTION");
		sql.ShouldContain("DROP VIEW");
		sql.ShouldContain("DROP TABLE");
		sql.ShouldContain("DROP COLUMN system_period");
	}

	[Fact]
	void Disabling_drops_the_dependents_before_what_they_depend_on()
	{
		// PostgreSQL refuses each of these in any other order: the triggers depend on the function,
		// the view on the history table and on system_period.
		var sql = DisableSql();

		var order = (string[])
		[
			"DROP TRIGGER \"transition_widget_versioning_insert\" ON \"public\".\"transition_widget\"",
			"DROP TRIGGER \"transition_widget_versioning_update\" ON \"public\".\"transition_widget\"",
			"DROP TRIGGER \"transition_widget_versioning_delete\" ON \"public\".\"transition_widget\"",
			"DROP FUNCTION \"public\".\"transition_widget_versioning\"()",
			"DROP VIEW \"public\".\"transition_widget_timeline\"",
			"DROP TABLE \"public\".\"transition_widget_history\"",
			"ALTER TABLE \"public\".\"transition_widget\" DROP COLUMN system_period"
		];
		var positions = order.Select(statement =>
		{
			var position = sql.IndexOf(statement, StringComparison.Ordinal);
			position.ShouldBeGreaterThanOrEqualTo(0, $"'{statement}' should have been emitted");
			return position;
		}).ToList();
		List<int> ascending = [.. positions.Order()];
		positions.ShouldBe(ascending, "the teardown order is the only appliable one");
	}

	[Fact]
	void Disabling_emits_no_prelude_and_no_apparatus()
	{
		// Tearing the apparatus down needs neither the PG19 floor nor btree_gist.
		var sql = DisableSql();

		sql.ShouldNotContain("server_version_num");
		sql.ShouldNotContain("btree_gist");
		sql.ShouldNotContain("CREATE ");
	}

	[Fact]
	void A_table_alteration_that_leaves_the_marker_alone_is_no_transition()
	{
		// A table that is temporal before and after still produces an AlterTableOperation for an
		// unrelated change; rebuilding the apparatus on top of itself would fail on the first CREATE.
		using TemporalContext from = new(Options<TemporalContext>());
		using CommentedTemporalContext to = new(Options<CommentedTemporalContext>());

		var sql = TransitionSql(from, to);

		sql.ShouldContain("COMMENT ON TABLE", Case.Insensitive, "the alteration itself should still be emitted");
		sql.ShouldNotContain("system_period");
	}

	[Fact]
	void Enabling_without_a_declared_schema_asserts_the_session_default_schema()
	{
		// The apparatus is qualified "public" while Npgsql leaves the main table to the search path;
		// the enable path owes the same assert the create path does.
		EnableSql().ShouldContain("pg_catalog.current_schema() <> 'public'");
	}

	[Fact]
	void Enabling_under_a_declared_schema_skips_the_default_schema_assert()
	{
		using DeclaredSchemaPlainContext from = new(Options<DeclaredSchemaPlainContext>());
		using DeclaredSchemaTemporalContext to = new(Options<DeclaredSchemaTemporalContext>());

		var sql = TransitionSql(from, to);

		sql.ShouldNotContain("current_schema()");
		sql.ShouldContain("CREATE TABLE \"norse_audit\".\"transition_widget_history\"");
	}

	[Fact]
	void Enabling_emits_the_prelude_again_for_the_next_batch_off_the_same_generator()
	{
		// The once-per-migration flag lives on the generator, which EF resolves per context scope, so
		// the batch-level reset is what keeps a second migration's floor assert from going missing.
		using PlainContext from = new(Options<PlainContext>());
		using TemporalContext to = new(Options<TemporalContext>());

		TransitionSql(from, to);
		var second = TransitionSql(from, to);

		Occurrences(second, "current_setting('server_version_num')::int").ShouldBe(1);
	}

	[Fact]
	void Enabling_alongside_an_added_column_on_the_same_table_fails_by_name()
	{
		// The apparatus mirrors the target shape, so CREATE VIEW would select a column AddColumn has not
		// put on the main table yet. Loud at scaffold time beats a migration that dies half-applied.
		using PlainContext from = new(Options<PlainContext>());
		using TemporalPlusContext to = new(Options<TemporalPlusContext>());

		var exception = Should.Throw<InvalidOperationException>(() => TransitionSql(from, to));

		exception.Message.ShouldContain("enabled on table 'transition_widget'");
		exception.Message.ShouldContain("AddColumn 'access_count'");
		exception.Message.ShouldContain("its own migration");
	}

	[Fact]
	void Disabling_alongside_a_dropped_column_on_the_same_table_fails_by_name()
	{
		// The differ sorts DROP COLUMN ahead of the alteration, so it would run while the timeline view
		// still selects that column.
		using TemporalPlusContext from = new(Options<TemporalPlusContext>());
		using PlainContext to = new(Options<PlainContext>());

		var exception = Should.Throw<InvalidOperationException>(() => TransitionSql(from, to));

		exception.Message.ShouldContain("disabled on table 'transition_widget'");
		exception.Message.ShouldContain("DropColumn 'access_count'");
		exception.Message.ShouldContain("its own migration");
	}

	[Fact]
	void Renaming_a_table_while_enabling_temporality_in_the_same_migration_fails_by_name()
	{
		// Generate(RenameTableOperation…) keys temporality off the TARGET model, so unguarded this would
		// fire the rename choreography (DROP VIEW, history-table rename, trigger retirement) against
		// apparatus that was never built under the old name 'foo' — measured directly against the real
		// differ before this guard existed: DropPrimaryKeyOperation | RenameTableOperation |
		// AlterTableOperation | AddPrimaryKeyOperation, and the generator produced two competing
		// CREATE TABLE "bar_history" statements.
		using RenameFooUnmarkedContext from = new(Options<RenameFooUnmarkedContext>());
		using RenameBarMarkedContext to = new(Options<RenameBarMarkedContext>());

		var exception = Should.Throw<InvalidOperationException>(() => TransitionSql(from, to));

		exception.Message.ShouldContain("enabled on table 'bar'");
		exception.Message.ShouldContain("renames it from 'foo'");
		exception.Message.ShouldContain("its own migration");
	}

	[Fact]
	void Renaming_a_table_while_disabling_temporality_in_the_same_migration_fails_by_name()
	{
		// The mirror shape: measured directly against the real differ before this guard existed, the
		// disable teardown ran under the table's new name 'bar' while the trigger/function/history-table
		// apparatus was still bound to the old name 'foo' — DROP TRIGGER/FUNCTION/VIEW/TABLE all targeted
		// names that were never created, and PostgreSQL would refuse every one of them.
		using RenameFooMarkedContext from = new(Options<RenameFooMarkedContext>());
		using RenameBarUnmarkedContext to = new(Options<RenameBarUnmarkedContext>());

		var exception = Should.Throw<InvalidOperationException>(() => TransitionSql(from, to));

		exception.Message.ShouldContain("disabled on table 'bar'");
		exception.Message.ShouldContain("renames it from 'foo'");
		exception.Message.ShouldContain("its own migration");
	}

	static string EnableSql()
	{
		using PlainContext from = new(Options<PlainContext>());
		using TemporalContext to = new(Options<TemporalContext>());
		return TransitionSql(from, to);
	}

	static string DisableSql()
	{
		using TemporalContext from = new(Options<TemporalContext>());
		using PlainContext to = new(Options<PlainContext>());
		return TransitionSql(from, to);
	}

	// The real differ over two real models, then the real generator over what it produced: the marker
	// transition is never hand-built, so a change in how EF surfaces it fails here rather than passing
	// against a fabricated operation.
	static string TransitionSql(DbContext from, DbContext to)
	{
		var targetModel = to.GetService<IDesignTimeModel>().Model;
		var operations = to.GetService<IMigrationsModelDiffer>().GetDifferences(
			from.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
			targetModel.GetRelationalModel());
		var commands = to.GetService<IMigrationsSqlGenerator>().Generate(operations, targetModel);
		return string.Join(Environment.NewLine, commands.Select(command => command.CommandText));
	}

	static DbContextOptions<TContext> Options<TContext>() where TContext : DbContext
	{
		DbContextOptionsBuilder<TContext> optionsBuilder = new();
		optionsBuilder.ApplyNorseProviderOptions(NorsePostgresEfProvider.Instance,
			NorsePostgresEfProvider.Instance.DesignTimePlaceholderConnectionString("norse_test"),
			migrationsAssemblyName: null);
		return optionsBuilder.Options;
	}

	static int Occurrences(string sql, string value) =>
		sql.Split(value).Length - 1;

	sealed class PlainContext(DbContextOptions<PlainContext> options) : NorseDbContext(options)
	{
		public DbSet<PlainRow> Rows => Set<PlainRow>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<PlainRow>().ToTable(TransitionTable);
		}
	}

	sealed class TemporalContext(DbContextOptions<TemporalContext> options) : NorseDbContext(options)
	{
		public DbSet<TemporalRow> Rows => Set<TemporalRow>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<TemporalRow>().ToTable(TransitionTable);
		}
	}

	// Marked and one column wider than the others: diffed against PlainContext it yields a column
	// operation and a marker transition on the same table, in one batch, in either direction.
	sealed class TemporalPlusContext(DbContextOptions<TemporalPlusContext> options) : NorseDbContext(options)
	{
		public DbSet<TemporalRowPlus> Rows => Set<TemporalRowPlus>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<TemporalRowPlus>().ToTable(TransitionTable);
		}
	}

	sealed class CommentedTemporalContext(DbContextOptions<CommentedTemporalContext> options)
		: NorseDbContext(options)
	{
		public DbSet<TemporalRow> Rows => Set<TemporalRow>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<TemporalRow>()
				.ToTable(TransitionTable, static table => table.HasComment("Still temporal, merely commented."));
		}
	}

	// The declared-schema variants are their own context types rather than a constructor flag: EF caches
	// the built model per context type, so a flagged context would silently reuse whichever model the
	// first test through it happened to build.
	sealed class DeclaredSchemaPlainContext(DbContextOptions<DeclaredSchemaPlainContext> options)
		: NorseDbContext(options)
	{
		public DbSet<PlainRow> Rows => Set<PlainRow>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.HasDefaultSchema(DeclaredSchema);
			builder.Entity<PlainRow>().ToTable(TransitionTable);
		}
	}

	sealed class DeclaredSchemaTemporalContext(DbContextOptions<DeclaredSchemaTemporalContext> options)
		: NorseDbContext(options)
	{
		public DbSet<TemporalRow> Rows => Set<TemporalRow>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.HasDefaultSchema(DeclaredSchema);
			builder.Entity<TemporalRow>().ToTable(TransitionTable);
		}
	}

	sealed record PlainRow : INorseEntity<PlainRow>
	{
		public int Id { get; init; }

		[MaxLength(100)] public string Name { get; init; } = "";

		public static void Configure(EntityTypeBuilder<PlainRow> builder)
		{
		}
	}

	sealed record TemporalRow : ITemporalEntity, INorseEntity<TemporalRow>
	{
		public int Id { get; init; }

		[MaxLength(100)] public string Name { get; init; } = "";

		public static void Configure(EntityTypeBuilder<TemporalRow> builder)
		{
		}
	}

	sealed record TemporalRowPlus : ITemporalEntity, INorseEntity<TemporalRowPlus>
	{
		public int Id { get; init; }

		[MaxLength(100)] public string Name { get; init; } = "";

		public int AccessCount { get; init; }

		public static void Configure(EntityTypeBuilder<TemporalRowPlus> builder)
		{
		}
	}

	// The differ pairs a rename only when the same CLR entity type maps to both table names (measured:
	// two different CLR types mapped to two different table names diff as DropTableOperation +
	// CreateTableOperation, never a rename). But ITemporalEntity is a compile-time trait of the CLR type,
	// so no pair of ordinarily-built contexts can share one entity type across a rename AND disagree on
	// temporality. The four contexts below share the single RenameTransitionRow type and set
	// Norse:Temporal with HasAnnotation directly instead of the marker interface — the same annotation
	// TemporalEntityConvention stamps in production and the same one NorseNpgsqlAnnotationProvider reads,
	// so the differ still performs the real rename pairing and the generator still reads the real
	// annotation; only the stamping mechanism is substituted.
	sealed class RenameFooUnmarkedContext(DbContextOptions<RenameFooUnmarkedContext> options)
		: NorseDbContext(options)
	{
		public DbSet<RenameTransitionRow> Rows => Set<RenameTransitionRow>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<RenameTransitionRow>().ToTable("foo");
		}
	}

	sealed class RenameBarMarkedContext(DbContextOptions<RenameBarMarkedContext> options)
		: NorseDbContext(options)
	{
		public DbSet<RenameTransitionRow> Rows => Set<RenameTransitionRow>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<RenameTransitionRow>().ToTable("bar")
				.HasAnnotation(NorseAnnotationNames.Temporal, true);
		}
	}

	sealed class RenameFooMarkedContext(DbContextOptions<RenameFooMarkedContext> options)
		: NorseDbContext(options)
	{
		public DbSet<RenameTransitionRow> Rows => Set<RenameTransitionRow>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<RenameTransitionRow>().ToTable("foo")
				.HasAnnotation(NorseAnnotationNames.Temporal, true);
		}
	}

	sealed class RenameBarUnmarkedContext(DbContextOptions<RenameBarUnmarkedContext> options)
		: NorseDbContext(options)
	{
		public DbSet<RenameTransitionRow> Rows => Set<RenameTransitionRow>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<RenameTransitionRow>().ToTable("bar");
		}
	}

	sealed record RenameTransitionRow : INorseEntity<RenameTransitionRow>
	{
		public int Id { get; init; }

		[MaxLength(100)] public string Name { get; init; } = "";

		public static void Configure(EntityTypeBuilder<RenameTransitionRow> builder)
		{
		}
	}
}
