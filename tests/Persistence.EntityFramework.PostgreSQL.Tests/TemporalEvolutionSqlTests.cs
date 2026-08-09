namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

/// <summary>
///     DDL coverage for evolution against a temporal table (spec §3.4). The fixed order — drop the timeline
///     view, run the main-table operation, mirror it onto history, regenerate the function, recreate the
///     view — is ruling 16, and it is the only appliable order: PostgreSQL refuses a column drop or a type
///     change under a dependent view, and <c>CREATE OR REPLACE VIEW</c> cannot change the output column set.
///     The arrange is EF's real model differ over two real models (<see cref="TemporalEvolution" />), so the
///     operations are never hand-built.
/// </summary>
public sealed class TemporalEvolutionSqlTests
{
	[Fact]
	void Every_evolution_batch_drops_the_view_first_and_recreates_it_last()
	{
		var sql = TemporalEvolution.AddColumnSql();

		var dropView = Position(sql, """DROP VIEW "public"."temporal_widgets_timeline";""");
		var mainAlter = Position(sql, "ALTER TABLE public.temporal_widgets ADD extra");
		var historyAlter = Position(sql, """ALTER TABLE "public"."temporal_widgets_history" ADD""");
		var function = Position(sql, "CREATE OR REPLACE FUNCTION");
		var createView = Position(sql, """CREATE VIEW "public"."temporal_widgets_timeline""");

		dropView.ShouldBeLessThan(mainAlter);
		mainAlter.ShouldBeLessThan(historyAlter);
		historyAlter.ShouldBeLessThan(function);
		function.ShouldBeLessThan(createView);
	}

	[Fact]
	void Add_column_mirrors_name_and_store_type_onto_history_and_nothing_else()
	{
		// The main column is NOT NULL with a default; the history mirror is nullable and bare — the
		// projection rule is name and store type only (spec §3.4).
		var sql = TemporalEvolution.AddColumnSql();

		sql.ShouldContain(
			"""ALTER TABLE "public"."temporal_widgets_history" ADD COLUMN "extra" character varying(50);""");
		sql.ShouldNotContain("""ADD COLUMN "extra" character varying(50) NOT NULL""");
	}

	[Fact]
	void Drop_column_mirrors_the_drop_onto_history_in_the_fixed_order()
	{
		var sql = TemporalEvolution.DropColumnSql();

		var dropView = Position(sql, """DROP VIEW "public"."temporal_widgets_timeline";""");
		var mainDrop = Position(sql, "ALTER TABLE public.temporal_widgets DROP COLUMN extra;");
		var historyDrop = Position(sql,
			"""ALTER TABLE "public"."temporal_widgets_history" DROP COLUMN "extra";""");
		var createView = Position(sql, """CREATE VIEW "public"."temporal_widgets_timeline""");

		dropView.ShouldBeLessThan(mainDrop);
		mainDrop.ShouldBeLessThan(historyDrop);
		historyDrop.ShouldBeLessThan(createView);
	}

	[Fact]
	void Rename_column_renames_on_history_and_never_drops_and_adds()
	{
		// Rename, not drop plus add: history data mapping is preserved (spec §3.4).
		var sql = TemporalEvolution.RenameColumnSql();

		sql.ShouldContain(
			"""ALTER TABLE "public"."temporal_widgets_history" RENAME COLUMN "name" TO "label";""");
		sql.ShouldNotContain("DROP COLUMN");
		sql.ShouldNotContain("ADD COLUMN");
	}

	[Fact]
	void Rename_column_regenerates_the_function_and_view_over_the_new_name()
	{
		var sql = TemporalEvolution.RenameColumnSql();

		sql.ShouldContain("CREATE OR REPLACE FUNCTION \"public\".\"temporal_widgets_versioning\"()");
		sql.ShouldContain("""INSERT INTO "public"."temporal_widgets_history" ("id", "label", system_period)""");
		sql.ShouldContain("SELECT \"id\", \"label\", system_period FROM \"public\".\"temporal_widgets\"");
		// The old name survives only in the rename statements themselves, never in the regenerated apparatus.
		sql.ShouldNotContain("OLD.\"name\"");
	}

	[Fact]
	void Alter_column_type_mirrors_onto_history_in_the_fixed_order()
	{
		var sql = TemporalEvolution.AlterColumnTypeSql();

		var dropView = Position(sql, """DROP VIEW "public"."temporal_widgets_timeline";""");
		var mainAlter = Position(sql, "ALTER TABLE public.temporal_widgets ALTER COLUMN name TYPE");
		var historyAlter = Position(sql,
			"""ALTER TABLE "public"."temporal_widgets_history" ALTER COLUMN "name" TYPE character varying(200);""");
		var createView = Position(sql, """CREATE VIEW "public"."temporal_widgets_timeline""");

		dropView.ShouldBeLessThan(mainAlter);
		mainAlter.ShouldBeLessThan(historyAlter);
		historyAlter.ShouldBeLessThan(createView);
	}

	[Fact]
	void Alter_column_never_projects_nullability_onto_history()
	{
		// History columns are nullable except the temporal key components, whatever the main table says.
		var sql = TemporalEvolution.AlterColumnTypeSql();

		sql.ShouldNotContain("""ALTER TABLE "public"."temporal_widgets_history" ALTER COLUMN "name" SET NOT NULL""");
		sql.ShouldNotContain("""ALTER TABLE "public"."temporal_widgets_history" ALTER COLUMN "name" DROP NOT NULL""");
	}

	[Fact]
	void Two_column_operations_on_one_table_share_one_drop_and_one_recreate()
	{
		// The view is rebuilt from the TARGET column list, so recreating it after the first of two adds
		// would select a column the second add has not made yet — unappliable DDL that snapshots alone
		// would never catch.
		var sql = TemporalEvolution.TwoAddedColumnsSql();

		Occurrences(sql, """DROP VIEW "public"."temporal_widgets_timeline";""").ShouldBe(1);
		Occurrences(sql, """CREATE VIEW "public"."temporal_widgets_timeline""").ShouldBe(1);
		var createView = Position(sql, """CREATE VIEW "public"."temporal_widgets_timeline""");
		Position(sql, "ALTER TABLE public.temporal_widgets ADD note").ShouldBeLessThan(createView);
		Position(sql, """ALTER TABLE "public"."temporal_widgets_history" ADD COLUMN "note""")
			.ShouldBeLessThan(createView);
	}

	[Fact]
	void Rename_table_retires_the_old_apparatus_and_creates_the_new_in_order()
	{
		// PostgreSQL keeps a renamed table's triggers under their old names, still bound to the old
		// function; creating a newly named function rebinds nothing. Only the tables rename in place.
		var sql = TemporalEvolution.RenameTableSql();

		var dropView = Position(sql, """DROP VIEW "public"."temporal_widgets_timeline";""");
		var renameMain = Position(sql, "ALTER TABLE public.temporal_widgets RENAME TO renamed_widgets;");
		var renameHistory = Position(sql,
			"""ALTER TABLE "public"."temporal_widgets_history" RENAME TO "renamed_widgets_history";""");
		var dropTrigger = Position(sql, """DROP TRIGGER "temporal_widgets_versioning_update""");
		var dropFunction = Position(sql, """DROP FUNCTION "public"."temporal_widgets_versioning"()""");
		var newFunction = Position(sql, """CREATE FUNCTION "public"."renamed_widgets_versioning"()""");
		var newTrigger = Position(sql, """CREATE TRIGGER "renamed_widgets_versioning_update""");
		var newView = Position(sql, """CREATE VIEW "public"."renamed_widgets_timeline""");

		dropView.ShouldBeLessThan(renameMain);
		renameMain.ShouldBeLessThan(renameHistory);
		renameHistory.ShouldBeLessThan(dropTrigger);
		dropTrigger.ShouldBeLessThan(dropFunction);
		dropFunction.ShouldBeLessThan(newFunction);
		newFunction.ShouldBeLessThan(newTrigger);
		newTrigger.ShouldBeLessThan(newView);
	}

	[Fact]
	void Rename_table_drops_the_old_triggers_off_the_already_renamed_table()
	{
		// The trigger keeps its old name but lives on the new table by the time it is dropped.
		var sql = TemporalEvolution.RenameTableSql();

		sql.ShouldContain(
			"""DROP TRIGGER "temporal_widgets_versioning_update" ON "public"."renamed_widgets";""");
		sql.ShouldContain(
			"""DROP TRIGGER "temporal_widgets_versioning_delete" ON "public"."renamed_widgets";""");
	}

	[Fact]
	void Rename_table_never_recreates_the_history_table()
	{
		// History data mapping is preserved: the table renames, it is not rebuilt.
		var sql = TemporalEvolution.RenameTableSql();

		sql.ShouldNotContain("CREATE TABLE");
		sql.ShouldNotContain("DROP TABLE");
	}

	[Fact]
	void Rename_table_hands_the_view_to_a_column_operation_in_the_same_batch()
	{
		// EF sorts the rename ahead of an added column, so a view built by the rename from the target
		// column list would select a column no ADD COLUMN has run yet. The rename creates the function and
		// the triggers and leaves the view to the column operation that finishes the shape.
		var sql = TemporalEvolution.RenameTableWithAddedColumnSql();

		Occurrences(sql, """CREATE VIEW "public"."renamed_widgets_timeline""").ShouldBe(1);
		Position(sql, "ALTER TABLE \"public\".\"renamed_widgets_history\" ADD COLUMN \"extra\"")
			.ShouldBeLessThan(Position(sql, "CREATE VIEW \"public\".\"renamed_widgets_timeline\""));
	}

	[Fact]
	void A_column_dropped_ahead_of_its_table_rename_still_mirrors_under_the_old_name()
	{
		// The other ordering: EF sorts a dropped column BEFORE the rename, and the operation still carries
		// the old table name — which the target model has never heard of. Identified through the batch's
		// rename map, emitted against the name the table actually has at that point.
		var sql = TemporalEvolution.RenameTableWithDroppedColumnSql();

		var dropView = Position(sql, """DROP VIEW "public"."temporal_widgets_timeline";""");
		var historyDrop = Position(sql,
			"""ALTER TABLE "public"."temporal_widgets_history" DROP COLUMN "name";""");
		dropView.ShouldBeLessThan(historyDrop);
		historyDrop.ShouldBeLessThan(
			Position(sql, """ALTER TABLE "public"."temporal_widgets_history" RENAME TO "renamed_widgets_history";"""));
		sql.ShouldContain("""CREATE VIEW "public"."renamed_widgets_timeline""");
	}

	[Fact]
	void Column_operations_on_both_sides_of_a_rename_share_one_group()
	{
		// EF sorts the dropped column ahead of the rename (old name) and the added one after it (new
		// name). Grouped by the name each operation happens to carry, that is two groups, and the first
		// finishes early — emitting the view over the full target shape while the ADD COLUMN it names has
		// not run. One group per target table is the only shape that survives.
		var sql = TemporalEvolution.RenameTableWithDroppedAndAddedColumnSql();

		Occurrences(sql, """DROP VIEW "public"."temporal_widgets_timeline";""").ShouldBe(1);
		Occurrences(sql, """CREATE VIEW "public"."temporal_widgets_timeline""").ShouldBe(0);
		Occurrences(sql, """CREATE VIEW "public"."renamed_widgets_timeline""").ShouldBe(1);

		var dropView = Position(sql, """DROP VIEW "public"."temporal_widgets_timeline";""");
		var historyDrop = Position(sql,
			"""ALTER TABLE "public"."temporal_widgets_history" DROP COLUMN "name";""");
		var renameHistory = Position(sql,
			"""ALTER TABLE "public"."temporal_widgets_history" RENAME TO "renamed_widgets_history";""");
		var historyAdd = Position(sql,
			"""ALTER TABLE "public"."renamed_widgets_history" ADD COLUMN "extra" character varying(50);""");
		var createView = Position(sql, """CREATE VIEW "public"."renamed_widgets_timeline""");

		dropView.ShouldBeLessThan(historyDrop);
		historyDrop.ShouldBeLessThan(renameHistory);
		renameHistory.ShouldBeLessThan(historyAdd);
		historyAdd.ShouldBeLessThan(createView);
	}

	[Fact]
	void A_column_dropped_ahead_of_its_rename_leaves_the_apparatus_to_the_rename()
	{
		// The pre-rename side of the batch owns no recreate: the apparatus it would build carries the old
		// name and the rename retires it two statements later. One function, one view, both new-named.
		var sql = TemporalEvolution.RenameTableWithDroppedColumnSql();

		Occurrences(sql, """CREATE VIEW "public"."temporal_widgets_timeline""").ShouldBe(0);
		Occurrences(sql, "FUNCTION \"public\".\"temporal_widgets_versioning\"()").ShouldBe(1,
			"only the DROP FUNCTION of the rename choreography");
		Occurrences(sql, """CREATE VIEW "public"."renamed_widgets_timeline""").ShouldBe(1);
	}

	[Fact]
	void Rename_table_passes_the_primary_key_constraint_rename_through()
	{
		// A rename renames the primary-key CONSTRAINT too, and EF has no rename-constraint operation, so
		// it says so as a drop and an add over unchanged columns. That is a rename's collateral, not a key
		// change, and the rejection guard has to know the difference.
		var sql = TemporalEvolution.RenameTableSql();

		sql.ShouldContain("ALTER TABLE public.temporal_widgets DROP CONSTRAINT pk_temporal_widgets;");
		sql.ShouldContain("ALTER TABLE public.renamed_widgets ADD CONSTRAINT pk_renamed_widgets PRIMARY KEY (id);");
	}

	[Fact]
	void Dropping_the_entity_tears_the_apparatus_down_before_the_table()
	{
		// The entity leaves the model, so the target model holds nothing to consult — the marker rides the
		// DropTableOperation itself, projected by the migrations annotation provider's ForRemove. The view
		// depends on the main table, so a bare DROP TABLE is refused outright (2BP01): teardown comes first,
		// in the drop-view-first order every other evolution shape uses.
		var sql = TemporalEvolution.DropEntitySql();

		var dropView = Position(sql, """DROP VIEW "public"."temporal_widgets_timeline";""");
		var dropTrigger = Position(sql,
			"""DROP TRIGGER "temporal_widgets_versioning_update" ON "public"."temporal_widgets";""");
		var dropFunction = Position(sql, """DROP FUNCTION "public"."temporal_widgets_versioning"();""");
		var dropHistory = Position(sql, """DROP TABLE "public"."temporal_widgets_history";""");
		var dropMain = Position(sql, "DROP TABLE public.temporal_widgets;");

		dropView.ShouldBeLessThan(dropTrigger);
		dropTrigger.ShouldBeLessThan(dropFunction);
		dropFunction.ShouldBeLessThan(dropHistory);
		dropHistory.ShouldBeLessThan(dropMain);
	}

	[Fact]
	void Dropping_the_entity_destroys_recorded_history_visibly()
	{
		// Same posture as disabling temporality (spec §3.3/§3.4): the destruction is explicit statements in
		// the scaffolded diff, never a helper call that hides what is being thrown away.
		var sql = TemporalEvolution.DropEntitySql();

		sql.ShouldContain("""DROP TABLE "public"."temporal_widgets_history";""");
		sql.ShouldNotContain("server_version_num");
		sql.ShouldNotContain("btree_gist");
	}

	[Fact]
	void Dropping_a_non_temporal_entity_passes_through_untouched()
	{
		var sql = TemporalEvolution.PlainDropEntitySql();

		sql.ShouldContain("DROP TABLE public.plain_widgets;");
		sql.ShouldNotContain("_history");
		sql.ShouldNotContain("_timeline");
		sql.ShouldNotContain("_versioning");
	}

	[Fact]
	void A_primary_key_change_on_a_temporal_table_is_rejected_with_a_named_diagnostic()
	{
		var exception = Should.Throw<InvalidOperationException>(TemporalEvolution.KeyChangeSql);

		exception.Message.ShouldContain("temporal_widgets");
		exception.Message.ShouldContain("PrimaryKey");
		exception.Message.ShouldContain("drop temporality");
	}

	[Fact]
	void A_schema_move_on_a_temporal_table_is_rejected_with_a_named_diagnostic()
	{
		var exception = Should.Throw<InvalidOperationException>(TemporalEvolution.SchemaMoveSql);

		exception.Message.ShouldContain("temporal_widgets");
		exception.Message.ShouldContain("archive");
		exception.Message.ShouldContain("drop temporality");
	}

	[Fact]
	void Evolution_emits_no_prelude()
	{
		// The floor assert and the extension guard belong to the paths that BUILD the apparatus; a column
		// change on a table that already has it needs neither, and a mirror aimed at the wrong schema
		// fails on its own missing relation rather than silently landing somewhere plausible.
		var sql = TemporalEvolution.AddColumnSql();

		sql.ShouldNotContain("server_version_num");
		sql.ShouldNotContain("btree_gist");
	}

	[Fact]
	void Column_operations_on_a_non_temporal_table_pass_through_untouched()
	{
		var sql = TemporalEvolution.PlainAddColumnSql();

		sql.ShouldContain("ALTER TABLE public.plain_widgets ADD extra");
		sql.ShouldNotContain("_history");
		sql.ShouldNotContain("_timeline");
		sql.ShouldNotContain("_versioning");
	}

	[Fact]
	void Renaming_a_non_temporal_table_passes_through_untouched()
	{
		var sql = TemporalEvolution.PlainRenameTableSql();

		sql.ShouldContain("ALTER TABLE public.plain_widgets RENAME TO renamed_plain_widgets;");
		sql.ShouldNotContain("_history");
		sql.ShouldNotContain("_timeline");
		sql.ShouldNotContain("_versioning");
	}

	[Fact]
	void A_primary_key_change_on_a_non_temporal_table_is_not_rejected()
	{
		var sql = TemporalEvolution.PlainKeyChangeSql();

		sql.ShouldContain("PRIMARY KEY");
	}

	static int Position(string sql, string statement)
	{
		var position = sql.IndexOf(statement, StringComparison.Ordinal);
		position.ShouldBeGreaterThanOrEqualTo(0, $"'{statement}' should have been emitted");
		return position;
	}

	static int Occurrences(string sql, string value) =>
		sql.Split(value).Length - 1;
}
