using Microsoft.EntityFrameworkCore;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

// EF1002: every string reaching these calls is either the generator's own DDL or a relation name this
// file declares as a literal. Schema identifiers cannot be parameterized in any case, and no user input
// exists anywhere near a test that builds its own database.
#pragma warning disable EF1002

/// <summary>
/// Every evolution shape of spec §3.4, applied to a real PostgreSQL 19beta2 server with a live history
/// row and a live timeline view already in place. The snapshot suite proves the SQL says what the design
/// says; this one proves PostgreSQL accepts it — the failure the design will not ship (ruling 16) is DDL
/// that reads correctly and cannot be applied, and it dies here rather than in the integration suite.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(PostgresCollection.Name)]
public sealed class TemporalEvolutionLiveTests(PostgresContainerFixture fixture)
{
	static CancellationToken Cancellation => TestContext.Current.CancellationToken;

	[Fact]
	async Task Add_column_applies_against_a_live_history_table_and_view()
	{
		await using var context = await StartAsync<MarkedContext>("evolution_add_column",
			static options => new MarkedContext(options));
		await SeedAndVersionAsync(context, "temporal_widgets", []);

		await context.Database.ExecuteSqlRawAsync(TemporalEvolution.AddColumnSql(), Cancellation);

		// The view is queryable again, and the history row predating the column honestly says NULL.
		(await CountAsync(context, "temporal_widgets_timeline")).ShouldBe(2);
		(await CountAsync(context, "temporal_widgets_history WHERE extra IS NULL")).ShouldBe(1);
	}

	[Fact]
	async Task Two_added_columns_apply_as_one_batch()
	{
		// The view is rebuilt from the target column list; recreating it after the first of the two adds
		// would select a column the second add has not made yet, and PostgreSQL would refuse it here.
		await using var context = await StartAsync<MarkedContext>("evolution_two_columns",
			static options => new MarkedContext(options));
		await SeedAndVersionAsync(context, "temporal_widgets", []);

		await context.Database.ExecuteSqlRawAsync(TemporalEvolution.TwoAddedColumnsSql(), Cancellation);

		(await CountAsync(context, "temporal_widgets_timeline")).ShouldBe(2);
		await context.Database.ExecuteSqlRawAsync("UPDATE public.temporal_widgets SET note = 'n';",
			Cancellation);
		(await CountAsync(context, "temporal_widgets_history")).ShouldBe(2);
	}

	[Fact]
	async Task Drop_column_applies_against_a_live_history_table_and_view()
	{
		// The view depends on the column being dropped, which is the whole reason for drop-view-first.
		await using var context = await StartAsync<MarkedPlusContext>("evolution_drop_column",
			static options => new MarkedPlusContext(options));
		await SeedAndVersionAsync(context, "temporal_widgets", [("extra", "x")]);

		await context.Database.ExecuteSqlRawAsync(TemporalEvolution.DropColumnSql(), Cancellation);

		(await CountAsync(context, "temporal_widgets_timeline")).ShouldBe(2);
	}

	[Fact]
	async Task Rename_column_applies_and_the_recorded_values_follow_the_column()
	{
		await using var context = await StartAsync<MarkedContext>("evolution_rename_column",
			static options => new MarkedContext(options));
		await SeedAndVersionAsync(context, "temporal_widgets", []);

		await context.Database.ExecuteSqlRawAsync(TemporalEvolution.RenameColumnSql(), Cancellation);

		// Renamed, never dropped and re-added: the closed version still carries its recorded value.
		(await CountAsync(context, "temporal_widgets_history WHERE label = 'v1'")).ShouldBe(1);
		(await CountAsync(context, "temporal_widgets_timeline")).ShouldBe(2);
	}

	[Fact]
	async Task Alter_column_type_applies_against_a_live_history_table_and_view()
	{
		await using var context = await StartAsync<MarkedContext>("evolution_alter_column",
			static options => new MarkedContext(options));
		await SeedAndVersionAsync(context, "temporal_widgets", []);

		await context.Database.ExecuteSqlRawAsync(TemporalEvolution.AlterColumnTypeSql(), Cancellation);

		(await CountAsync(context, "temporal_widgets_timeline")).ShouldBe(2);
	}

	[Fact]
	async Task Rename_table_rebinds_triggers_to_the_new_function_with_new_names()
	{
		// PostgreSQL keeps a renamed table's triggers under their old names, still bound to the old
		// function. This is the assertion that the apparatus was genuinely retired and rebuilt.
		await using var context = await StartAsync<MarkedContext>("evolution_rename_table",
			static options => new MarkedContext(options));
		await SeedAndVersionAsync(context, "temporal_widgets", []);

		await context.Database.ExecuteSqlRawAsync(TemporalEvolution.RenameTableSql(), Cancellation);

		(await TriggerBindingsAsync(context, "renamed_widgets")).ShouldBe(
		[
			"renamed_widgets_versioning_delete -> renamed_widgets_versioning",
			"renamed_widgets_versioning_update -> renamed_widgets_versioning"
		]);
		// Versioning survives the rename: a further update closes another version into the renamed history.
		await context.Database.ExecuteSqlRawAsync("UPDATE public.renamed_widgets SET name = 'v3';",
			Cancellation);
		(await CountAsync(context, "renamed_widgets_history")).ShouldBe(2);
		(await CountAsync(context, "renamed_widgets_timeline")).ShouldBe(3);
	}

	[Fact]
	async Task Rename_table_and_add_column_apply_in_one_batch()
	{
		// The rename choreography and the fixed column order both touch the same timeline view; if EF
		// sorted the column operation ahead of the rename, one of them would reach for a view under a name
		// that does not exist yet. Proved against the server rather than assumed.
		await using var context = await StartAsync<MarkedContext>("evolution_rename_and_add",
			static options => new MarkedContext(options));
		await SeedAndVersionAsync(context, "temporal_widgets", []);

		await context.Database.ExecuteSqlRawAsync(TemporalEvolution.RenameTableWithAddedColumnSql(),
			Cancellation);

		(await CountAsync(context, "renamed_widgets_timeline")).ShouldBe(2);
		await context.Database.ExecuteSqlRawAsync("UPDATE public.renamed_widgets SET extra = 'x';",
			Cancellation);
		(await CountAsync(context, "renamed_widgets_history")).ShouldBe(2);
	}

	[Fact]
	async Task Rename_table_and_drop_column_apply_in_one_batch()
	{
		await using var context = await StartAsync<MarkedContext>("evolution_rename_and_drop",
			static options => new MarkedContext(options));
		await SeedAndVersionAsync(context, "temporal_widgets", []);

		await context.Database.ExecuteSqlRawAsync(TemporalEvolution.RenameTableWithDroppedColumnSql(),
			Cancellation);

		(await CountAsync(context, "renamed_widgets_timeline")).ShouldBe(2);
	}

	[Fact]
	async Task Rename_table_with_column_operations_on_both_sides_applies_in_one_batch()
	{
		// The three-way batch: EF sorts the dropped column ahead of the rename and the added one after it.
		// Two groups would finish the first one early and select the added column before it exists.
		await using var context = await StartAsync<MarkedContext>("evolution_rename_and_swap",
			static options => new MarkedContext(options));
		await SeedAndVersionAsync(context, "temporal_widgets", []);

		await context.Database.ExecuteSqlRawAsync(
			TemporalEvolution.RenameTableWithDroppedAndAddedColumnSql(), Cancellation);

		(await CountAsync(context, "renamed_widgets_timeline")).ShouldBe(2);
		await context.Database.ExecuteSqlRawAsync("UPDATE public.renamed_widgets SET extra = 'x';",
			Cancellation);
		(await CountAsync(context, "renamed_widgets_history")).ShouldBe(2);
	}

	[Fact]
	async Task Dropping_the_entity_removes_the_table_and_every_apparatus_object()
	{
		// A bare DROP TABLE is refused under the dependent timeline view (2BP01), and the versioning
		// function would outlive its table besides. Nothing derived from the table may survive it.
		await using var context = await StartAsync<MarkedContext>("evolution_drop_entity",
			static options => new MarkedContext(options));
		await SeedAndVersionAsync(context, "temporal_widgets", []);
		// The apparatus is genuinely standing first, so the assertions below cannot pass vacuously.
		(await RelationsAsync(context, "temporal_widgets%")).ShouldBe(
			["temporal_widgets", "temporal_widgets_history", "temporal_widgets_timeline"]);

		await context.Database.ExecuteSqlRawAsync(TemporalEvolution.DropEntitySql(), Cancellation);

		(await RelationsAsync(context, "temporal_widgets%")).ShouldBeEmpty();
		(await FunctionsAsync(context, "temporal_widgets%")).ShouldBeEmpty();
	}

	// The schema the transition SQL will be applied to, standing on the real server through the same
	// generator the snapshots run through — EnsureCreated routes to IMigrationsSqlGenerator, so the
	// apparatus arrives with the tables.
	async Task<TContext> StartAsync<TContext>(string database,
		Func<DbContextOptions<TContext>, TContext> create) where TContext : DbContext
	{
		var connectionString = await fixture.CreateDatabaseAsync(database, Cancellation);
		var context = create(TemporalEvolution.LiveOptions<TContext>(connectionString));
		await context.Database.EnsureCreatedAsync(Cancellation);
		return context;
	}

	// One row and one update: a live history row and a live view for the evolution to run against.
	static async Task SeedAndVersionAsync(DbContext context, string table,
		IReadOnlyList<(string Column, string Value)> extraColumns)
	{
		var columns = string.Join(", ", extraColumns.Select(column => column.Column).Prepend("name"));
		var values = string.Join(", ", extraColumns.Select(column => $"'{column.Value}'").Prepend("'v1'"));
		await context.Database.ExecuteSqlRawAsync(
			$"INSERT INTO public.{table} ({columns}) VALUES ({values});", Cancellation);
		await context.Database.ExecuteSqlRawAsync($"UPDATE public.{table} SET name = 'v2';", Cancellation);
	}

	static Task<long> CountAsync(DbContext context, string relation) =>
		context.Database.SqlQueryRaw<long>($"SELECT count(*) AS \"Value\" FROM public.{relation}")
			.SingleAsync(Cancellation);

	// Main table, history table, and timeline view alike: pg_class carries all three, so one query proves
	// the whole apparatus rather than three that could each pass while another object lingers. Restricted
	// to ordinary tables and views ('r', 'v') because indexes and sequences live in pg_class too and cannot
	// outlive the table they belong to. Triggers need no check of their own for the same reason.
	static Task<List<string>> RelationsAsync(DbContext context, string pattern) =>
		context.Database.SqlQueryRaw<string>(
			$"""
			SELECT c.relname AS "Value"
			FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
			WHERE n.nspname = 'public' AND c.relkind IN ('r', 'v') AND c.relname LIKE '{pattern}'
			ORDER BY c.relname
			""").ToListAsync(Cancellation);

	static Task<List<string>> FunctionsAsync(DbContext context, string pattern) =>
		context.Database.SqlQueryRaw<string>(
			$"""
			SELECT p.proname AS "Value"
			FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
			WHERE n.nspname = 'public' AND p.proname LIKE '{pattern}'
			ORDER BY p.proname
			""").ToListAsync(Cancellation);

	static Task<List<string>> TriggerBindingsAsync(DbContext context, string table) =>
		context.Database.SqlQueryRaw<string>(
			$"""
			SELECT t.tgname || ' -> ' || p.proname AS "Value"
			FROM pg_trigger t JOIN pg_proc p ON p.oid = t.tgfoid
			WHERE t.tgrelid = 'public.{table}'::regclass AND NOT t.tgisinternal
			ORDER BY t.tgname
			""").ToListAsync(Cancellation);
}
