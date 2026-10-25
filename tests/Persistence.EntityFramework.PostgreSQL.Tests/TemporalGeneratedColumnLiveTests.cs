using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

// EF1002: every string reaching ExecuteSqlRawAsync is the generator's own DDL.
#pragma warning disable EF1002

/// <summary>
///     Stored generated columns on temporal tables and the chassis-owned <c>norse_utf8_sha256</c>, against a
///     real <c>postgres:19rc1</c> server. Identity PoC G6 proved the shape with a hand-written stand-in; this
///     suite proves the chassis's own generated DDL does the same: a no-op update writes nothing even though
///     <c>NEW.&lt;generated&gt;</c> is <c>NULL</c> in the <c>BEFORE</c> trigger, a real update carries the
///     materialized value into history from <c>OLD</c>, and the function migrates, is <c>IMMUTABLE</c>, and
///     re-applies idempotently.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(PostgresCollection.Name)]
public sealed class TemporalGeneratedColumnLiveTests(PostgresContainerFixture fixture)
{
	const string Hashed = TemporalGeneratedColumns.HashedTable;

	const string Widgets = TemporalEvolution.Table;

	static CancellationToken Cancellation => TestContext.Current.CancellationToken;

	[Fact]
	async Task No_op_update_on_a_table_with_a_generated_column_writes_no_history_row()
	{
		await using var live = await StartAsync<HashedWidgetContext>("generated_no_op", static options => new(options));
		var id = await SeedAsync(live, "unchanged");
		var before = await live.SystemPeriodTextAsync(Hashed, id);

		await live.ExecuteAsync($"UPDATE public.{Hashed} SET name = 'unchanged' WHERE id = $1", id);

		(await live.HistoryAsync(Hashed, id)).ShouldBeEmpty();
		(await live.SystemPeriodTextAsync(Hashed, id)).ShouldBe(before);
	}

	[Fact]
	async Task Generated_column_value_reaches_history_on_a_real_update()
	{
		await using var live = await StartAsync<HashedWidgetContext>("generated_history", static options => new(options));
		var id = await SeedAsync(live, "before");
		var start = await live.ServerClockAsync();

		await live.ExecuteAsync($"UPDATE public.{Hashed} SET name = 'after' WHERE id = $1", id);

		var history = (await live.HistoryAsync(Hashed, id)).ShouldHaveSingleItem();
		history.Name.ShouldBe("before");
		history.Upper!.Value.ShouldBeGreaterThanOrEqualTo(start);
		(await HistoryHashAsync(live, Hashed, id, "before")).ShouldBe(Sha256("before"));
		(await CurrentHashAsync(live, Hashed, id)).ShouldBe(Sha256("after"));
		// The projection rule, read off the catalog: generated on the main table, plain in history.
		(await GeneratedKindAsync(live, Hashed)).ShouldBe("s");
		(await GeneratedKindAsync(live, $"{Hashed}_history")).ShouldBe("");
	}

	[Fact]
	async Task The_chassis_function_migrates_immutable_and_hashes_utf8()
	{
		await using var live = await StartAsync<HashedWidgetContext>("generated_function", static options => new(options));
		const string Value = "Þórr ᚦ 雷神";

		var id = await SeedAsync(live, Value);

		(await VolatilityAsync(live)).ShouldBe(["i"]);
		(await CurrentHashAsync(live, Hashed, id)).ShouldBe(Sha256(Value));
		// EF never names the generated column in its INSERT, and reads the computed value back.
		live.Context.Widgets.Single(widget => widget.Id == id).NameHash.ShouldBe(Sha256(Value));
	}

	[Fact]
	async Task Adding_a_generated_column_to_a_live_temporal_table_versions_it_with_the_exclusion()
	{
		await using var live = await StartAsync<MarkedContext>("generated_add_column", static options => new(options));
		await live.ExecuteAsync($"INSERT INTO public.{Widgets} (name) VALUES ('v1')");
		await live.ExecuteAsync($"UPDATE public.{Widgets} SET name = 'v2'");
		var id = await live.ScalarAsync<int>($"SELECT id FROM public.{Widgets}");

		await live.Context.Database.ExecuteSqlRawAsync(TemporalGeneratedColumns.AddGeneratedColumnSql(), Cancellation);

		// The version predating the column honestly says NULL; the current row was computed by the rewrite.
		(await live.CountAsync($"{Widgets}_history WHERE name = 'v1' AND name_hash IS NULL")).ShouldBe(1);
		(await CurrentHashAsync(live, Widgets, id)).ShouldBe(Sha256("v2"));
		await live.ExecuteAsync($"UPDATE public.{Widgets} SET name = 'v2'");
		(await live.CountAsync($"{Widgets}_history")).ShouldBe(1, "the regenerated function must exclude the hash");
		await live.ExecuteAsync($"UPDATE public.{Widgets} SET name = 'v3'");
		(await HistoryHashAsync(live, Widgets, id, "v2")).ShouldBe(Sha256("v2"));
		(await live.CountAsync($"{Widgets}_timeline")).ShouldBe(3);
	}

	[Fact]
	async Task Altering_a_live_column_to_generated_keeps_its_recorded_history()
	{
		await using var live = await StartAsync<MarkedRawHashContext>("generated_alter_column",
			static options => new(options));
		await live.ExecuteAsync($"INSERT INTO public.{Widgets} (name, name_hash) VALUES ('v1', '\\x01')");
		await live.ExecuteAsync($"UPDATE public.{Widgets} SET name = 'v2'");
		var id = await live.ScalarAsync<int>($"SELECT id FROM public.{Widgets}");

		await live.Context.Database.ExecuteSqlRawAsync(TemporalGeneratedColumns.AlterToGeneratedColumnSql(),
			Cancellation);

		// Npgsql rebuilds the main column; the history column is never rebuilt, so its values survive.
		(await HistoryHashAsync(live, Widgets, id, "v1")).ShouldBe([0x01]);
		(await CurrentHashAsync(live, Widgets, id)).ShouldBe(Sha256("v2"));
		(await GeneratedKindAsync(live, Widgets)).ShouldBe("s");
		await live.ExecuteAsync($"UPDATE public.{Widgets} SET name = 'v2'");
		(await live.CountAsync($"{Widgets}_history")).ShouldBe(1, "the regenerated function must exclude the hash");
		await live.ExecuteAsync($"UPDATE public.{Widgets} SET name = 'v3'");
		(await HistoryHashAsync(live, Widgets, id, "v2")).ShouldBe(Sha256("v2"));
	}

	[Fact]
	async Task A_second_migration_naming_the_function_again_applies_idempotently()
	{
		// The first migration creates the function and a generated column that depends on it; the second
		// names it again and re-emits CREATE OR REPLACE over a function that now has a dependent.
		await using var live = await StartAsync<MarkedContext>("generated_idempotent", static options => new(options));
		await live.Context.Database.ExecuteSqlRawAsync(TemporalGeneratedColumns.AddGeneratedColumnSql(), Cancellation);
		using PlainHashedWidgetContext second = new(TemporalEvolution.Options<PlainHashedWidgetContext>());
		var script = second.Database.GenerateCreateScript();
		script.ShouldContain(TemporalGeneratedColumns.FunctionSql);

		await live.Context.Database.ExecuteSqlRawAsync(script, Cancellation);

		(await VolatilityAsync(live)).ShouldBe(["i"]);
		await live.ExecuteAsync($"INSERT INTO public.{TemporalGeneratedColumns.PlainHashedTable} (name) VALUES ('x')");
		await live.ExecuteAsync($"INSERT INTO public.{Widgets} (name) VALUES ('y')");
		(await live.ScalarAsync<byte[]>(
			$"SELECT name_hash FROM public.{TemporalGeneratedColumns.PlainHashedTable}")).ShouldBe(Sha256("x"));
		(await live.ScalarAsync<byte[]>($"SELECT name_hash FROM public.{Widgets}")).ShouldBe(Sha256("y"));
	}

	static byte[] Sha256(string value) =>
		SHA256.HashData(Encoding.UTF8.GetBytes(value));

	static async Task<int> SeedAsync(TemporalDatabase<HashedWidgetContext> live, string name)
	{
		HashedWidget widget = new() { Name = name };
		live.Context.Widgets.Add(widget);
		await live.Context.SaveChangesAsync(Cancellation);
		return widget.Id;
	}

	static Task<byte[]> CurrentHashAsync<TContext>(TemporalDatabase<TContext> live, string table, int id)
		where TContext : DbContext =>
		live.ScalarAsync<byte[]>($"SELECT name_hash FROM public.{table} WHERE id = $1", id);

	static Task<byte[]> HistoryHashAsync<TContext>(TemporalDatabase<TContext> live, string table, int id,
		string name) where TContext : DbContext =>
		live.ScalarAsync<byte[]>($"SELECT name_hash FROM public.{table}_history WHERE id = $1 AND name = $2", id,
			name);

	static Task<string> GeneratedKindAsync<TContext>(TemporalDatabase<TContext> live, string table)
		where TContext : DbContext
	{
		return live.ScalarAsync<string>(
			"""
			SELECT attgenerated::text FROM pg_catalog.pg_attribute
			WHERE attrelid = $1::regclass AND attname = 'name_hash' AND NOT attisdropped
			""", $"public.{table}");
	}

	static async Task<List<string>> VolatilityAsync<TContext>(TemporalDatabase<TContext> live)
		where TContext : DbContext
	{
		await using var connection = await live.OpenAsync();
		await using var command = TemporalSql.Command(connection,
			"""
			SELECT p.provolatile::text FROM pg_catalog.pg_proc p
			JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
			WHERE n.nspname = 'public' AND p.proname = 'norse_utf8_sha256'
			""");
		await using var reader = await command.ExecuteReaderAsync(Cancellation);
		List<string> values = [];
		while (await reader.ReadAsync(Cancellation))
			values.Add(reader.GetString(0));
		return values;
	}

	async Task<TemporalDatabase<TContext>> StartAsync<TContext>(string database,
		Func<DbContextOptions<TContext>, TContext> create) where TContext : DbContext
	{
		var connectionString = await fixture.CreateDatabaseAsync(database, Cancellation);
		var context = create(TemporalEvolution.LiveOptions<TContext>(connectionString));
		await context.Database.EnsureCreatedAsync(Cancellation);
		return new TemporalDatabase<TContext>(context, connectionString);
	}
}
