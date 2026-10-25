using Microsoft.EntityFrameworkCore;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

/// <summary>
///     DDL snapshot coverage for stored generated columns on temporal tables and for the chassis-owned
///     <c>norse_utf8_sha256</c> function. In a <c>BEFORE UPDATE</c> trigger <c>NEW.&lt;generated&gt;</c> is
///     <c>NULL</c> (stored generated columns are computed after <c>BEFORE</c> triggers), so the no-op
///     comparison must leave it out or every update is a change (chassis spec §3.2, amended). <c>OLD</c> holds
///     the materialized value, so the history copy takes it from there into a plain history column, and no
///     write to the main table ever names it (§3.4, amended; <c>428C9</c>). Every trigger regeneration path
///     applies the same rule: create, enable, and column evolution.
/// </summary>
public sealed class TemporalGeneratedColumnSqlTests
{
	const string HashedComparison =
		"""IF TG_OP = 'UPDATE' AND ROW(OLD."id", OLD."name") IS NOT DISTINCT FROM ROW(NEW."id", NEW."name") THEN""";

	const string HashedHistoryInsert =
		"""
		INSERT INTO "public"."hashed_widgets_history" ("id", "name", "name_hash", system_period)
				VALUES (OLD."id", OLD."name", OLD."name_hash", pg_catalog.tstzrange(pg_catalog.lower(OLD.system_period), ts));
		""";

	const string WidgetsComparison =
		"""IF TG_OP = 'UPDATE' AND ROW(OLD."id", OLD."name") IS NOT DISTINCT FROM ROW(NEW."id", NEW."name") THEN""";

	const string WidgetsHistoryInsert =
		"""
		INSERT INTO "public"."temporal_widgets_history" ("id", "name", "name_hash", system_period)
				VALUES (OLD."id", OLD."name", OLD."name_hash", pg_catalog.tstzrange(pg_catalog.lower(OLD.system_period), ts));
		""";

	static string CreateScript<TContext>(Func<DbContextOptions<TContext>, TContext> create)
		where TContext : DbContext
	{
		using var context = create(TemporalEvolution.Options<TContext>());
		return context.Database.GenerateCreateScript();
	}

	static string HashedScript() =>
		CreateScript<HashedWidgetContext>(static options => new(options));

	[Fact]
	void No_op_comparison_excludes_stored_generated_columns()
	{
		var script = HashedScript();

		script.ShouldContain(HashedComparison);
		script.ShouldNotContain("NEW.\"name_hash\"");
	}

	[Fact]
	void History_insert_copies_generated_columns_from_OLD()
	{
		var script = HashedScript();

		script.ShouldContain(HashedHistoryInsert);
		// The projection rule: name and store type only. A generated history column would refuse the copy.
		script.ShouldContain("\t\"name_hash\" bytea,");
		Occurrences(script, "GENERATED ALWAYS AS").ShouldBe(1, "only the main table's column is generated");
	}

	[Fact]
	void The_timeline_view_selects_the_generated_column_from_both_tables()
	{
		var script = HashedScript();

		script.ShouldContain(
			"""SELECT "id", "name", "name_hash", system_period FROM "public"."hashed_widgets_history";""");
	}

	[Fact]
	void Computed_column_naming_norse_utf8_sha256_emits_the_function_first()
	{
		var script = HashedScript();

		var floorAssert = Position(script, "current_setting('server_version_num')::int");
		var function = Position(script, TemporalGeneratedColumns.FunctionSql);
		var createTable = Position(script, "CREATE TABLE public.hashed_widgets (");

		floorAssert.ShouldBeLessThan(function);
		function.ShouldBeLessThan(createTable);
		Occurrences(script, "norse_utf8_sha256(text)").ShouldBe(1);
	}

	[Fact]
	void An_unmarked_table_naming_the_function_gets_it_too()
	{
		var script = CreateScript<PlainHashedWidgetContext>(static options => new(options));

		Position(script, TemporalGeneratedColumns.FunctionSql)
			.ShouldBeLessThan(Position(script, "CREATE TABLE public.plain_hashed_widgets ("));
		script.ShouldNotContain("system_period");
	}

	[Fact]
	void Two_tables_naming_the_function_in_one_batch_emit_it_once()
	{
		var script = CreateScript<TwoHashedWidgetContext>(static options => new(options));

		Occurrences(script, "norse_utf8_sha256(text)").ShouldBe(1);
	}

	[Fact]
	void A_generated_column_that_does_not_name_the_function_gets_no_function()
	{
		var script = CreateScript<LoweredWidgetContext>(static options => new(options));

		script.ShouldNotContain("norse_utf8_sha256");
		script.ShouldContain(
			"""IF TG_OP = 'UPDATE' AND ROW(OLD."id", OLD."name") IS NOT DISTINCT FROM ROW(NEW."id", NEW."name") THEN""");
	}

	[Fact]
	void A_table_with_no_generated_column_gets_no_function() =>
		CreateScript<MarkedContext>(static options => new(options)).ShouldNotContain("norse_utf8_sha256");

	[Fact]
	void Adding_a_generated_column_regenerates_the_trigger_function_with_the_exclusion()
	{
		var sql = TemporalGeneratedColumns.AddGeneratedColumnSql();

		var function = Position(sql, TemporalGeneratedColumns.FunctionSql);
		var mainAdd = Position(sql, "ALTER TABLE public.temporal_widgets ADD name_hash");
		var historyAdd = Position(sql,
			"""ALTER TABLE "public"."temporal_widgets_history" ADD COLUMN "name_hash" bytea;""");
		var versioning = Position(sql, "CREATE OR REPLACE FUNCTION \"public\".\"temporal_widgets_versioning\"()");

		function.ShouldBeLessThan(mainAdd);
		mainAdd.ShouldBeLessThan(historyAdd);
		historyAdd.ShouldBeLessThan(versioning);
		sql.ShouldContain(WidgetsComparison);
		sql.ShouldContain(WidgetsHistoryInsert);
	}

	[Fact]
	void Altering_a_column_to_generated_emits_the_function_ahead_and_regenerates_with_the_exclusion()
	{
		var sql = TemporalGeneratedColumns.AlterToGeneratedColumnSql();

		Position(sql, TemporalGeneratedColumns.FunctionSql)
			.ShouldBeLessThan(Position(sql, "ALTER TABLE public.temporal_widgets"));
		sql.ShouldContain(WidgetsComparison);
		sql.ShouldContain(WidgetsHistoryInsert);
	}

	[Fact]
	void Altering_a_column_to_generated_rebuilds_the_main_column_and_never_the_history_column()
	{
		// Npgsql realizes this alteration as a DROP COLUMN plus an ADD COLUMN of the main column, issued
		// through the generator's own overrides. Those nested calls are base emission only: the history
		// column keeps its recorded values and takes the type mirror, and the evolution runs its fixed
		// order exactly once.
		var sql = TemporalGeneratedColumns.AlterToGeneratedColumnSql();

		sql.ShouldNotContain("""ALTER TABLE "public"."temporal_widgets_history" DROP COLUMN""");
		sql.ShouldNotContain("""ALTER TABLE "public"."temporal_widgets_history" ADD COLUMN""");
		Occurrences(sql, """DROP VIEW "public"."temporal_widgets_timeline";""").ShouldBe(1);
		Occurrences(sql, "CREATE OR REPLACE FUNCTION \"public\".\"temporal_widgets_versioning\"()").ShouldBe(1);
		Occurrences(sql, "CREATE VIEW \"public\".\"temporal_widgets_timeline\"").ShouldBe(1);
	}

	[Fact]
	void Enabling_on_a_table_with_a_generated_column_applies_the_exclusion()
	{
		var sql = TemporalGeneratedColumns.EnableWithGeneratedColumnSql();

		sql.ShouldContain(WidgetsComparison);
		sql.ShouldContain(WidgetsHistoryInsert);
		// The column already exists, so nothing here names the function: the migration that created the
		// column created the function too.
		sql.ShouldNotContain("norse_utf8_sha256");
	}

	static int Occurrences(string script, string value) =>
		script.Split(value).Length - 1;

	static int Position(string script, string statement)
	{
		var position = script.IndexOf(statement, StringComparison.Ordinal);
		position.ShouldBeGreaterThanOrEqualTo(0, $"'{statement}' should have been emitted in:\n{script}");
		return position;
	}
}
