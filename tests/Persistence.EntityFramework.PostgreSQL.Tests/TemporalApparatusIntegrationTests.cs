using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

// CA2100: every command text below is either the generator's own DDL or a literal this file declares;
// the only values that vary are bound as parameters. EF1002 says the same thing about ExecuteSqlRawAsync.
#pragma warning disable CA2100, EF1002

/// <summary>
///     The runtime semantics of the PostgreSQL temporal apparatus, proved against a real
///     <c>postgres:19beta2</c> server (spec §6 item 3). The snapshot suites prove the generator emits the
///     design's SQL and the evolution live suite proves PostgreSQL accepts it; neither can say what the
///     apparatus <em>does</em> once rows start moving. That is this suite: one clock, the monotonicity
///     clamp, no-op suppression, and version closure (spec §3.2), plus the brownfield enable/disable
///     transitions (§3.3) against live data.
/// </summary>
/// <remarks>
///     Every timing assertion here is structural — positive period length, adjacency of a closed upper
///     bound to its successor's lower bound, overlap refused by the temporal primary key. Nothing compares
///     against wall clock or asserts a duration, because the clamp exists precisely so that correctness
///     does not depend on what the clock did.
/// </remarks>
/// <param name="fixture">The shared container.</param>
[Collection(PostgresCollection.Name)]
public sealed class TemporalApparatusIntegrationTests(PostgresContainerFixture fixture)
{
	const string Widgets = "split_temporal_widgets";

	const string Counters = "widget_counters";

	const string Brownfield = "brownfield_widgets";

	static CancellationToken Cancellation => TestContext.Current.CancellationToken;

	[Fact]
	async Task EnsureCreated_builds_the_full_apparatus_through_the_temporal_generator()
	{
		// The caveat the whole suite rests on: EnsureCreated routes CreateTable through
		// IMigrationsSqlGenerator, so the apparatus arrives with the tables and no scaffolded migration is
		// needed to stand one up. If this ever stops holding, every test below is arranging against a bare
		// table, and the fallback is to execute GenerateCreateScript() output directly.
		await using var live = await StartWidgetsAsync("temporal_ensure_created");

		(await live.RelationsAsync("split_temporal_widget%")).ShouldBe(
			[Widgets, $"{Widgets}_history", $"{Widgets}_timeline"]);
		(await live.FunctionsAsync("split_temporal_widget%")).ShouldBe([$"{Widgets}_versioning"]);
		(await live.TriggerBindingsAsync(Widgets)).ShouldBe(
		[
			$"{Widgets}_versioning_delete -> {Widgets}_versioning",
			$"{Widgets}_versioning_insert -> {Widgets}_versioning",
			$"{Widgets}_versioning_update -> {Widgets}_versioning"
		]);
		// The split fragment is a table like any other: no period column, no history, no triggers (§2.3).
		(await live.TriggerBindingsAsync(Counters)).ShouldBeEmpty();
		(await live.HasSystemPeriodAsync(Counters)).ShouldBeFalse();
	}

	[Fact]
	async Task An_insert_opens_a_current_version_and_writes_no_history()
	{
		await using var live = await StartWidgetsAsync("temporal_insert");
		var before = DateTimeOffset.UtcNow;

		var id = await SeedWidgetAsync(live, "before");

		var current = await live.CurrentAsync(Widgets, id);
		current.Name.ShouldBe("before");
		current.IsClosed.ShouldBeFalse("an insert opens a version, it does not close one");
		current.IsEmpty.ShouldBeFalse();
		// The trigger-assigned open bound is fresh wall clock at the row, not a caller-chosen or
		// long-stale timestamp — the INSERT branch reads clock_timestamp() the same as the UPDATE
		// closure clamp does (§3.2 amendment, 2026-08-05).
		current.Lower.ShouldBeGreaterThanOrEqualTo(before);
		current.Lower.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);
		(await live.HistoryAsync(Widgets, id)).ShouldBeEmpty();
	}

	[Fact]
	async Task A_raw_insert_supplying_an_explicit_system_period_is_rejected()
	{
		// Codex remand (2026-08-05): system_period is database-owned on every verb, not UPDATE alone. A
		// raw INSERT naming the column can override the trigger-assigned open bound and fabricate a
		// backdated one — the next legitimate UPDATE would then mint a history row covering an era the
		// row never existed. The guard has to fire before the row lands.
		await using var live = await StartWidgetsAsync("temporal_insert_system_period");

		var exception = await Should.ThrowAsync<PostgresException>(() => live.ExecuteAsync(
			$"""
			 INSERT INTO public.{Widgets} (name, system_period)
			 VALUES ('smuggled', tstzrange('1990-01-01T00:00:00Z'::timestamptz, 'infinity'))
			 """));

		exception.MessageText.ShouldContain("system_period");
		exception.MessageText.ShouldContain("database-owned");
		(await live.CountAsync($"{Widgets} WHERE name = 'smuggled'")).ShouldBe(0,
			"the rejected insert must not land");
	}

	[Fact]
	async Task An_update_writes_a_closed_history_row()
	{
		await using var live = await StartWidgetsAsync("temporal_update");
		var id = await SeedWidgetAsync(live, "before");

		await live.ExecuteAsync($"UPDATE public.{Widgets} SET name = 'after' WHERE id = $1", id);

		var history = await live.HistoryAsync(Widgets, id);
		history.Count.ShouldBe(1);
		history[0].Name.ShouldBe("before");
		history[0].IsClosed.ShouldBeTrue();
		history[0].HasPositiveLength.ShouldBeTrue("the clamp guarantees a strictly positive period");
		// The closed upper bound is the successor's lower bound: gapless by arithmetic, not by luck.
		var current = await live.CurrentAsync(Widgets, id);
		current.Name.ShouldBe("after");
		current.Lower.ShouldBe(history[0].Upper!.Value);
	}

	[Fact]
	async Task A_delete_writes_the_final_closed_version()
	{
		await using var live = await StartWidgetsAsync("temporal_delete");
		var id = await SeedWidgetAsync(live, "doomed");

		await live.ExecuteAsync($"DELETE FROM public.{Widgets} WHERE id = $1", id);

		(await live.CountAsync($"{Widgets} WHERE id = $1", id)).ShouldBe(0);
		var history = await live.HistoryAsync(Widgets, id);
		history.Count.ShouldBe(1);
		history[0].Name.ShouldBe("doomed");
		history[0].IsClosed.ShouldBeTrue();
		history[0].HasPositiveLength.ShouldBeTrue();
		// The timeline is the closed span and nothing else — the row's whole life, still readable.
		var timeline = await live.TimelineAsync(Widgets, id);
		timeline.Count.ShouldBe(1);
		timeline[0].IsClosed.ShouldBeTrue();
	}

	[Fact]
	async Task Repeated_updates_in_one_transaction_yield_positive_length_contiguous_versions()
	{
		// Same-transaction churn is kept, not collapsed (§3.2). Under now() both closures would read the
		// transaction's start time, the second range would normalize to empty, and the WITHOUT OVERLAPS
		// key would silently admit it — the exact failure the clock ruling exists to prevent.
		await using var live = await StartWidgetsAsync("temporal_same_transaction");
		var id = await SeedWidgetAsync(live, "v1");

		await using (var connection = await live.OpenAsync())
		{
			await using var transaction = await connection.BeginTransactionAsync(Cancellation);
			await TemporalSql.ExecuteAsync(connection,
				$"UPDATE public.{Widgets} SET name = 'v2' WHERE id = $1", id);
			await TemporalSql.ExecuteAsync(connection,
				$"UPDATE public.{Widgets} SET name = 'v3' WHERE id = $1", id);
			await transaction.CommitAsync(Cancellation);
		}

		var history = await live.HistoryAsync(Widgets, id);
		history.Select(version => version.Name).ShouldBe(["v1", "v2"]);
		history.ShouldAllBe(version => version.HasPositiveLength);
		history[0].Upper!.Value.ShouldBe(history[1].Lower);
		var current = await live.CurrentAsync(Widgets, id);
		current.Name.ShouldBe("v3");
		history[1].Upper!.Value.ShouldBe(current.Lower);
	}

	[Fact]
	async Task A_lock_waiting_concurrent_update_closes_monotonically()
	{
		// The other half of the clock ruling: a transaction that began before a competing writer committed
		// closes with post-lock wall clock, never with its own start time. now() there would compute an
		// upper bound earlier than the version's own lower bound and raise a backwards-range error.
		await using var live = await StartWidgetsAsync("temporal_lock_wait");
		var id = await SeedWidgetAsync(live, "v1");

		await using var waiter = await live.OpenAsync();
		await using var transaction = await waiter.BeginTransactionAsync(Cancellation);
		// A real statement, so the transaction's snapshot and its start timestamp are genuinely fixed here.
		await TemporalSql.ExecuteAsync(waiter, $"SELECT 1 FROM public.{Widgets} WHERE id = $1", id);
		await live.ExecuteAsync($"UPDATE public.{Widgets} SET name = 'committed-elsewhere' WHERE id = $1", id);
		await TemporalSql.ExecuteAsync(waiter, $"UPDATE public.{Widgets} SET name = 'v3' WHERE id = $1", id);
		await transaction.CommitAsync(Cancellation);

		var history = await live.HistoryAsync(Widgets, id);
		history.Select(version => version.Name).ShouldBe(["v1", "committed-elsewhere"]);
		history.ShouldAllBe(version => version.HasPositiveLength);
		history[0].Upper!.Value.ShouldBe(history[1].Lower);
	}

	[Fact]
	async Task A_no_op_update_writes_no_history_and_leaves_the_period_untouched()
	{
		// Version-churn policy (§3.2): history records knowledge changes, not statement traffic.
		await using var live = await StartWidgetsAsync("temporal_no_op");
		var id = await SeedWidgetAsync(live, "unchanged");
		var before = await live.SystemPeriodTextAsync(Widgets, id);

		await live.ExecuteAsync($"UPDATE public.{Widgets} SET name = 'unchanged' WHERE id = $1", id);

		(await live.HistoryAsync(Widgets, id)).ShouldBeEmpty();
		(await live.SystemPeriodTextAsync(Widgets, id)).ShouldBe(before);
	}

	[Fact]
	async Task A_raw_update_of_system_period_alone_is_rejected()
	{
		// Codex P1: system_period is database-owned (§3.2). Raw SQL setting only that column, with no
		// application-column change, must not sail through the no-op guard and persist a caller-chosen
		// period — the trigger has to reject the write before it ever reaches that comparison.
		await using var live = await StartWidgetsAsync("temporal_system_period_direct");
		var id = await SeedWidgetAsync(live, "stable");
		var before = await live.SystemPeriodTextAsync(Widgets, id);

		var exception = await Should.ThrowAsync<PostgresException>(() => live.ExecuteAsync(
			$"UPDATE public.{Widgets} SET system_period = tstzrange(clock_timestamp(), 'infinity') WHERE id = $1",
			id));

		exception.MessageText.ShouldContain("system_period");
		exception.MessageText.ShouldContain("database-owned");
		(await live.SystemPeriodTextAsync(Widgets, id)).ShouldBe(before, "the rejected write must not land");
	}

	[Fact]
	async Task A_raw_update_of_an_application_column_together_with_system_period_is_rejected()
	{
		// The guard has to fire even when a legitimate application-column change rides along: the caller
		// cannot buy a smuggled system_period write by pairing it with real data.
		await using var live = await StartWidgetsAsync("temporal_system_period_with_column");
		var id = await SeedWidgetAsync(live, "before");
		var beforePeriod = await live.SystemPeriodTextAsync(Widgets, id);

		var exception = await Should.ThrowAsync<PostgresException>(() => live.ExecuteAsync(
			$"""
			 UPDATE public.{Widgets} SET name = 'tampered', system_period = tstzrange(clock_timestamp(), 'infinity')
			 WHERE id = $1
			 """, id));

		exception.MessageText.ShouldContain("system_period");
		exception.MessageText.ShouldContain("database-owned");
		(await live.CurrentAsync(Widgets, id)).Name.ShouldBe("before", "the smuggled write must not land either");
		(await live.SystemPeriodTextAsync(Widgets, id)).ShouldBe(beforePeriod);
	}

	[Fact]
	async Task A_fragment_only_update_writes_no_history_row()
	{
		// Split-table asymmetry (§2.3): the fragment carries no apparatus, so churning it cannot version
		// the main row — and the main row is not touched at all, so its period stands untouched too.
		await using var live = await StartWidgetsAsync("temporal_fragment_only");
		var id = await SeedWidgetAsync(live, "stable");
		var before = await live.SystemPeriodTextAsync(Widgets, id);

		await live.ExecuteAsync($"UPDATE public.{Counters} SET access_count = 7 WHERE id = $1", id);

		(await live.CountAsync($"{Counters} WHERE id = $1 AND access_count = 7", id)).ShouldBe(1);
		(await live.HistoryAsync(Widgets, id)).ShouldBeEmpty();
		(await live.SystemPeriodTextAsync(Widgets, id)).ShouldBe(before);
	}

	[Fact]
	async Task Two_updates_leave_a_gapless_overlap_free_timeline()
	{
		await using var live = await StartWidgetsAsync("temporal_timeline");
		var id = await SeedWidgetAsync(live, "v1");
		await live.ExecuteAsync($"UPDATE public.{Widgets} SET name = 'v2' WHERE id = $1", id);
		await live.ExecuteAsync($"UPDATE public.{Widgets} SET name = 'v3' WHERE id = $1", id);

		var timeline = await live.TimelineAsync(Widgets, id);

		timeline.Select(version => version.Name).ShouldBe(["v1", "v2", "v3"]);
		timeline.ShouldAllBe(version => version.HasPositiveLength);
		timeline[0].Upper!.Value.ShouldBe(timeline[1].Lower);
		timeline[1].Upper!.Value.ShouldBe(timeline[2].Lower);
		timeline[2].IsClosed.ShouldBeFalse("the current version stays open at infinity");
	}

	[Fact]
	async Task The_temporal_primary_key_refuses_an_overlapping_history_row()
	{
		// The structural backstop behind the arithmetic: even a hand-written INSERT cannot make two
		// versions of one key overlap.
		await using var live = await StartWidgetsAsync("temporal_overlap");
		var id = await SeedWidgetAsync(live, "v1");
		await live.ExecuteAsync($"UPDATE public.{Widgets} SET name = 'v2' WHERE id = $1", id);

		var exception = await Should.ThrowAsync<PostgresException>(() => live.ExecuteAsync(
			$"""
			 INSERT INTO public.{Widgets}_history (id, name, system_period)
			 SELECT id, name, tstzrange(lower(system_period), upper(system_period) + interval '1 hour')
			 FROM public.{Widgets}_history WHERE id = $1
			 """, id));

		exception.SqlState.ShouldBe(PostgresErrorCodes.ExclusionViolation);
	}

	[Fact]
	async Task Enabling_on_a_table_with_existing_rows_backfills_one_enable_timestamp()
	{
		// Brownfield adoption (§3.3) — Himinbjörg's real path. The table exists, holds rows, and takes the
		// marker; every pre-existing row must enter the timeline at one shared enable timestamp rather than
		// scattered across a table rewrite by a volatile column default.
		await using var live = await StartBrownfieldAsync("temporal_enable");
		foreach (var name in (string[])["a", "b", "c"])
			await live.ExecuteAsync($"INSERT INTO public.{Brownfield} (name) VALUES ($1)", name);

		await live.Context.Database.ExecuteSqlRawAsync(EnableSql(), Cancellation);

		(await live.CountAsync(Brownfield)).ShouldBe(3);
		(await live.ScalarAsync<long>(
			$"SELECT count(DISTINCT lower(system_period)) FROM public.{Brownfield}")).ShouldBe(1,
			"one captured timestamp stamps every pre-existing row");
		(await live.CountAsync($"{Brownfield} WHERE NOT ({TemporalVersion.StillOpenSql})")).ShouldBe(0,
			"every pre-existing row enters the timeline as a current version");
		(await live.CountAsync($"{Brownfield}_history")).ShouldBe(0,
			"the table's pre-temporal past is honestly unrecorded");

		// And from here it versions like any other temporal table.
		var id = await live.ScalarAsync<int>($"SELECT id FROM public.{Brownfield} WHERE name = 'a'");
		await live.ExecuteAsync($"UPDATE public.{Brownfield} SET name = 'a2' WHERE id = $1", id);

		var history = await live.HistoryAsync(Brownfield, id);
		history.Count.ShouldBe(1);
		history[0].Name.ShouldBe("a");
		history[0].HasPositiveLength.ShouldBeTrue();
		history[0].Upper!.Value.ShouldBe((await live.CurrentAsync(Brownfield, id)).Lower);
	}

	[Fact]
	async Task Disabling_tears_the_apparatus_down()
	{
		await using var live = await StartBrownfieldTemporalAsync("temporal_disable");
		await live.ExecuteAsync($"INSERT INTO public.{Brownfield} (name) VALUES ('a')");
		await live.ExecuteAsync($"UPDATE public.{Brownfield} SET name = 'b'");
		// Standing first, so the assertions below cannot pass vacuously.
		(await live.CountAsync($"{Brownfield}_history")).ShouldBe(1);

		await live.Context.Database.ExecuteSqlRawAsync(DisableSql(), Cancellation);

		(await live.RelationsAsync("brownfield_widget%")).ShouldBe([Brownfield]);
		(await live.FunctionsAsync("brownfield_widget%")).ShouldBeEmpty();
		(await live.TriggerBindingsAsync(Brownfield)).ShouldBeEmpty();
		(await live.HasSystemPeriodAsync(Brownfield)).ShouldBeFalse();
	}

	[Fact]
	void The_schema_dump_contains_the_apparatus()
	{
		// The free rider (§3.5): the DBA schema dump runs through the same generator, so it carries the
		// whole apparatus without the scaffolder knowing temporal tables exist. No database — the script is
		// the artifact under test.
		using SplitWidgetContext context = new(TemporalEvolution.Options<SplitWidgetContext>());

		var script = context.Database.GenerateCreateScript();

		script.ShouldContain("""ADD COLUMN system_period tstzrange NOT NULL;""");
		script.ShouldContain($"CREATE TABLE \"public\".\"{Widgets}_history\"");
		script.ShouldContain("PRIMARY KEY (\"id\", \"system_period\" WITHOUT OVERLAPS)");
		script.ShouldContain($"CREATE FUNCTION \"public\".\"{Widgets}_versioning\"()");
		script.ShouldContain($"CREATE TRIGGER \"{Widgets}_versioning_insert\"");
		script.ShouldContain($"CREATE TRIGGER \"{Widgets}_versioning_update\"");
		script.ShouldContain($"CREATE TRIGGER \"{Widgets}_versioning_delete\"");
		script.ShouldContain($"CREATE VIEW \"public\".\"{Widgets}_timeline\"");
		script.ShouldNotContain($"{Counters}_history");
	}

	/// <summary>
	///     Inserts through EF, so the split write path and the database-owned column default are both real.
	///     Typed to the split-widget database on purpose: it is the only context that maps this entity, and a
	///     helper that compiles against the brownfield database and throws at run time is a trap, not a helper.
	/// </summary>
	static async Task<int> SeedWidgetAsync(TemporalDatabase<SplitWidgetContext> live, string name)
	{
		SplitTemporalWidget widget = new() { Name = name, AccessCount = 1 };
		live.Context.Widgets.Add(widget);
		await live.Context.SaveChangesAsync(Cancellation);
		return widget.Id;
	}

	static string EnableSql()
	{
		using BrownfieldPlainContext from = new(TemporalEvolution.Options<BrownfieldPlainContext>());
		using BrownfieldTemporalContext to = new(TemporalEvolution.Options<BrownfieldTemporalContext>());
		return TemporalEvolution.TransitionSql(from, to);
	}

	static string DisableSql()
	{
		using BrownfieldTemporalContext from = new(TemporalEvolution.Options<BrownfieldTemporalContext>());
		using BrownfieldPlainContext to = new(TemporalEvolution.Options<BrownfieldPlainContext>());
		return TemporalEvolution.TransitionSql(from, to);
	}

	Task<TemporalDatabase<SplitWidgetContext>> StartWidgetsAsync(string database) =>
		StartAsync<SplitWidgetContext>(database, static options => new(options));

	Task<TemporalDatabase<BrownfieldPlainContext>> StartBrownfieldAsync(string database) =>
		StartAsync<BrownfieldPlainContext>(database, static options => new(options));

	Task<TemporalDatabase<BrownfieldTemporalContext>> StartBrownfieldTemporalAsync(string database) =>
		StartAsync<BrownfieldTemporalContext>(database, static options => new(options));

	// The schema arrives through EnsureCreated, which routes CreateTable through the same
	// IMigrationsSqlGenerator the snapshots run through — so the apparatus stands with the tables.
	async Task<TemporalDatabase<TContext>> StartAsync<TContext>(string database,
		Func<DbContextOptions<TContext>, TContext> create) where TContext : DbContext
	{
		var connectionString = await fixture.CreateDatabaseAsync(database, Cancellation);
		var context = create(TemporalEvolution.LiveOptions<TContext>(connectionString));
		await context.Database.EnsureCreatedAsync(Cancellation);
		return new TemporalDatabase<TContext>(context, connectionString);
	}
}

/// <summary>
///     One test's database: the EF context that built it, plus the raw access the assertions need.
///     <c>system_period</c> is outside the EF model by design (§3.2), so every period reading here is a
///     deliberate trip past EF rather than a gap in the mapping.
/// </summary>
/// <param name="context">The context whose <c>EnsureCreated</c> built the schema.</param>
/// <param name="connectionString">The connection string for that database.</param>
sealed class TemporalDatabase<TContext>(TContext context, string connectionString) : IAsyncDisposable
	where TContext : DbContext
{
	static CancellationToken Cancellation => TestContext.Current.CancellationToken;

	public TContext Context => context;

	public ValueTask DisposeAsync() =>
		context.DisposeAsync();

	/// <summary>A connection of the caller's own, for the tests that need a transaction they control.</summary>
	public async Task<NpgsqlConnection> OpenAsync()
	{
		NpgsqlConnection connection = new(connectionString);
		await connection.OpenAsync(Cancellation);
		return connection;
	}

	public async Task ExecuteAsync(string sql, params object[] arguments)
	{
		await using var connection = await OpenAsync();
		await TemporalSql.ExecuteAsync(connection, sql, arguments);
	}

	public async Task<T> ScalarAsync<T>(string sql, params object[] arguments)
	{
		await using var connection = await OpenAsync();
		await using var command = TemporalSql.Command(connection, sql, arguments);
		await using var reader = await command.ExecuteReaderAsync(Cancellation);
		(await reader.ReadAsync(Cancellation)).ShouldBeTrue($"'{sql}' should have returned a row");
		return await reader.GetFieldValueAsync<T>(0, Cancellation);
	}

	public Task<long> CountAsync(string relation, params object[] arguments) =>
		ScalarAsync<long>($"SELECT count(*) FROM public.{relation}", arguments);

	public Task<List<TemporalVersion>> HistoryAsync(string table, int id) =>
		VersionsAsync($"{table}_history", id);

	public Task<List<TemporalVersion>> TimelineAsync(string table, int id) =>
		VersionsAsync($"{table}_timeline", id);

	public async Task<TemporalVersion> CurrentAsync(string table, int id) =>
		(await VersionsAsync(table, id)).ShouldHaveSingleItem();

	public Task<string> SystemPeriodTextAsync(string table, int id) =>
		ScalarAsync<string>($"SELECT system_period::text FROM public.{table} WHERE id = $1", id);

	public Task<bool> HasSystemPeriodAsync(string table)
	{
		return ScalarAsync<bool>(
			"""
			SELECT EXISTS (
				SELECT 1 FROM pg_catalog.pg_attribute
				WHERE attrelid = $1::regclass AND attname = 'system_period' AND NOT attisdropped)
			""", $"public.{table}");
	}

	/// <summary>
	///     Ordinary tables and views only ('r', 'v'): indexes and sequences live in <c>pg_class</c> too and
	///     cannot outlive the table they belong to, so counting them would only add noise.
	/// </summary>
	public Task<List<string>> RelationsAsync(string pattern)
	{
		return ListAsync(
			"""
			SELECT c.relname
			FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
			WHERE n.nspname = 'public' AND c.relkind IN ('r', 'v') AND c.relname LIKE $1
			ORDER BY c.relname
			""", pattern);
	}

	public Task<List<string>> FunctionsAsync(string pattern)
	{
		return ListAsync(
			"""
			SELECT p.proname
			FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
			WHERE n.nspname = 'public' AND p.proname LIKE $1
			ORDER BY p.proname
			""", pattern);
	}

	/// <summary>
	///     Trigger name and the function it is bound to, together: a trigger surviving under its old name and
	///     still bound to a retired function is the failure a name-only check would sail past.
	/// </summary>
	public Task<List<string>> TriggerBindingsAsync(string table)
	{
		return ListAsync(
			"""
			SELECT t.tgname || ' -> ' || p.proname
			FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_proc p ON p.oid = t.tgfoid
			WHERE t.tgrelid = $1::regclass AND NOT t.tgisinternal
			ORDER BY t.tgname
			""", $"public.{table}");
	}

	async Task<List<string>> ListAsync(string sql, string argument)
	{
		await using var connection = await OpenAsync();
		await using var command = TemporalSql.Command(connection, sql, argument);
		await using var reader = await command.ExecuteReaderAsync(Cancellation);
		List<string> values = [];
		while (await reader.ReadAsync(Cancellation))
			values.Add(reader.GetString(0));
		return values;
	}

	async Task<List<TemporalVersion>> VersionsAsync(string relation, int id)
	{
		await using var connection = await OpenAsync();
		await using var command = TemporalSql.Command(connection,
			$"""
			 SELECT name,
			 	lower(system_period),
			 	CASE WHEN {TemporalVersion.StillOpenSql} THEN NULL ELSE upper(system_period) END,
			 	isempty(system_period)
			 FROM public.{relation}
			 WHERE id = $1
			 ORDER BY lower(system_period)
			 """, id);
		await using var reader = await command.ExecuteReaderAsync(Cancellation);
		List<TemporalVersion> versions = [];
		while (await reader.ReadAsync(Cancellation))
			versions.Add(new TemporalVersion(reader.GetString(0),
				await reader.GetFieldValueAsync<DateTimeOffset>(1, Cancellation),
				await reader.IsDBNullAsync(2, Cancellation) ?
					null :
					await reader.GetFieldValueAsync<DateTimeOffset>(2, Cancellation),
				reader.GetBoolean(3)));
		return versions;
	}
}

/// <summary>
///     Raw command plumbing, shared by <see cref="TemporalDatabase{TContext}" /> and by the tests that drive
///     a transaction on a connection of their own. Every varying value is bound, never interpolated.
/// </summary>
static class TemporalSql
{
	public static NpgsqlCommand Command(NpgsqlConnection connection, string sql,
		params object[] arguments)
	{
		NpgsqlCommand command = new(sql, connection);
		foreach (var argument in arguments)
			command.Parameters.Add(new NpgsqlParameter { Value = argument });
		return command;
	}

	public static async Task ExecuteAsync(NpgsqlConnection connection, string sql,
		params object[] arguments)
	{
		await using var command = Command(connection, sql, arguments);
		await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
	}
}

/// <summary>One row of a timeline: a name and the system period it was true for.</summary>
/// <param name="Name">The recorded value of the application column these tests version on.</param>
/// <param name="Lower">The period's open bound.</param>
/// <param name="Upper">The period's closed bound, or <see langword="null" /> while the version is current.</param>
/// <param name="IsEmpty">
///     Whether PostgreSQL normalized the range to <c>empty</c>. Empty ranges overlap nothing, so a
///     <c>WITHOUT OVERLAPS</c> key admits any number of them — which is why every positive-length assertion
///     here checks this rather than trusting the key alone.
/// </param>
readonly record struct TemporalVersion(string Name, DateTimeOffset Lower, DateTimeOffset? Upper, bool IsEmpty)
{
	/// <summary>
	///     The SQL predicate for "this version is still current". The apparatus opens a period at
	///     <c>tstzrange(clock_timestamp(), 'infinity')</c> — an upper bound that exists and holds the infinity
	///     timestamp, not an unbounded range — so <c>upper_inf()</c> answers <see langword="false" /> for every
	///     one of them and would call every live row closed.
	/// </summary>
	public const string StillOpenSql = "upper(system_period) = 'infinity'::timestamptz";

	public bool IsClosed => Upper.HasValue;

	public bool HasPositiveLength => !IsEmpty && (!Upper.HasValue || Upper.Value > Lower);
}

/// <summary>
///     The split model against a real server: no declared schema, exactly as the create-path snapshots
///     exercise it, so the session-default-schema assert the apparatus carries runs live too.
/// </summary>
sealed class SplitWidgetContext(DbContextOptions<SplitWidgetContext> options) : NorseDbContext(options)
{
	public DbSet<SplitTemporalWidget> Widgets => Set<SplitTemporalWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.Entity<SplitTemporalWidget>()
			.ToTable("split_temporal_widgets")
			.SplitToTable("widget_counters",
				static counters => counters.Property(widget => widget.AccessCount));
	}
}

// The brownfield pair: one table, identical column shape, and the temporal marker as the only difference
// the differ can see — which is what makes the transition SQL the enable/disable transition and nothing
// else.
sealed class BrownfieldPlainContext(DbContextOptions<BrownfieldPlainContext> options)
	: NorseDbContext(options)
{
	public DbSet<BrownfieldRow> Rows => Set<BrownfieldRow>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.Entity<BrownfieldRow>().ToTable("brownfield_widgets");
	}
}

sealed class BrownfieldTemporalContext(DbContextOptions<BrownfieldTemporalContext> options)
	: NorseDbContext(options)
{
	public DbSet<TemporalBrownfieldRow> Rows => Set<TemporalBrownfieldRow>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.Entity<TemporalBrownfieldRow>().ToTable("brownfield_widgets");
	}
}

sealed record BrownfieldRow : INorseEntity<BrownfieldRow>
{
	public int Id { get; init; }

	[MaxLength(100)] public string Name { get; init; } = "";

	public static void Configure(EntityTypeBuilder<BrownfieldRow> builder)
	{
	}
}

sealed record TemporalBrownfieldRow : ITemporalEntity, INorseEntity<TemporalBrownfieldRow>
{
	public int Id { get; init; }

	[MaxLength(100)] public string Name { get; init; } = "";

	public static void Configure(EntityTypeBuilder<TemporalBrownfieldRow> builder)
	{
	}
}
