using Microsoft.Data.SqlClient;
using static Norse.Persistence.EntityFramework.SqlServer.IntegrationTests.PrincipalTableOnlySchema;

namespace Norse.Persistence.EntityFramework.SqlServer.IntegrationTests;

/// <summary>
///     Why a nullable principal column made required is refused at generation (spec §6.2), proven on the engine:
///     with versioning on, SQL Server applies the <c>NOT NULL</c> to history as well, and refuses it while any
///     recorded version holds a <c>NULL</c> — which SQL Server's stock generator never looks at, backfilling the
///     current table only. And the manual sequence the diagnostic names applies, leaving versioning on and still
///     recording. The chassis never backfills history itself: it is the audit record.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(SqlServerCollection.Name)]
public sealed class PrincipalTableOnlyRequiredColumnTests(SqlServerContainerFixture fixture)
{
	const string SkipReason = "Requires a running Docker daemon on a host other than Windows on ARM.";

	// The engine's refusal of a NOT NULL that a history row's NULL violates, measured against SQL Server 2025
	// 17.0: the generic "cannot insert NULL", naming the history table. The chassis's diagnostic names it too.
	const int HistoryNullRefusal = 515;

	const int SystemVersioned = 2;

	// The diagnostic's fixed steps, verbatim: it must name them, and they must apply as written.
	const string Off = "ALTER TABLE [Users] SET (SYSTEM_VERSIONING = OFF);";

	const string AlterCurrent = "ALTER TABLE [Users] ALTER COLUMN [Extra] nvarchar(32) NOT NULL;";

	const string AlterHistory = $"ALTER TABLE [dbo].[{HistoryTable}] ALTER COLUMN [Extra] nvarchar(32) NOT NULL;";

	const string On =
		$"ALTER TABLE [Users] SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[{HistoryTable}], DATA_CONSISTENCY_CHECK = ON));";

	public static bool IsSqlServerTestable => SqlServerContainerFixture.IsTestable;

	static CancellationToken Cancellation => TestContext.Current.CancellationToken;

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Stock_nullable_to_required_fails_when_history_holds_a_null()
	{
		var (database, id) = await CreateWithNullOnlyInHistoryAsync("required_column_stock");

		var refusal = await Should.ThrowAsync<SqlException>(() =>
			database.MigrateWithStockGeneratorAsync<AddedColumnContext, RequiredColumnContext>(Cancellation));

		refusal.Number.ShouldBe(HistoryNullRefusal, refusal.Message);
		refusal.Message.ShouldContain($"dbo.{HistoryTable}", Case.Sensitive, "history, not the current table, refuses");
		(await IsNullableAsync(database, PrincipalTable)).ShouldBeTrue("the refused alter changed nothing");
		(await HistoryNullCountAsync(database, id)).ShouldBe(2, "recorded history is untouched");
		(await database.TemporalTypeAsync(PrincipalTable, Cancellation)).ShouldBe(SystemVersioned);
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Manual_sequence_from_the_diagnostic_applies()
	{
		var (database, id) = await CreateWithNullOnlyInHistoryAsync("required_column_manual");
		var diagnostic = (await Should.ThrowAsync<InvalidOperationException>(() =>
			database.MigrateAsync<AddedColumnContext, RequiredColumnContext>(Cancellation))).Message;
		(await IsNullableAsync(database, PrincipalTable)).ShouldBeTrue("the rejection emits no SQL at all");
		foreach (var statement in (string[])[Off, AlterCurrent, AlterHistory, On])
			diagnostic.ShouldContain(statement);

		// One batch per step; (2) and (3) are the author's own values — history's is a deliberate audit decision.
		foreach (var batch in (string[])
		[
			Off,
			$"UPDATE [{PrincipalTable}] SET [Extra] = 'x' WHERE [Extra] IS NULL;",
			$"UPDATE [dbo].[{HistoryTable}] SET [Extra] = 'unrecorded' WHERE [Extra] IS NULL;",
			AlterCurrent,
			AlterHistory,
			On
		])
			await database.ExecuteAsync(batch, [], Cancellation);

		(await database.TemporalTypeAsync(PrincipalTable, Cancellation)).ShouldBe(SystemVersioned);
		(await database.HistoryTableOfAsync(PrincipalTable, Cancellation)).ShouldBe(HistoryTable);
		(await IsNullableAsync(database, PrincipalTable)).ShouldBeFalse();
		(await IsNullableAsync(database, HistoryTable)).ShouldBeFalse();
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1", "v2"], "recorded versions survive");
		await database.ExecuteAsync($"UPDATE [{PrincipalTable}] SET [Name] = 'v3' WHERE [Id] = @p0", [id],
			Cancellation);
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1", "v2", "v2"], "history keeps recording");
	}

	// Two closed versions predate Extra's value and record NULL; the current row holds one, so the only NULLs a
	// NOT NULL could trip over are in history.
	async Task<(SqlServerDatabase Database, int Id)> CreateWithNullOnlyInHistoryAsync(string name)
	{
		Assert.SkipUnless(fixture.Available, fixture.UnavailableReason ?? SkipReason);
		SqlServerDatabase database = new(await fixture.CreateDatabaseAsync(name, Cancellation));
		await database.MigrateAsync<EmptyContext, AddedColumnContext>(Cancellation);
		var id = await database.SeedVersionedUserAsync("v1", "v2", Cancellation);
		await database.ExecuteAsync($"UPDATE [{PrincipalTable}] SET [Extra] = 'x' WHERE [Id] = @p0", [id],
			Cancellation);
		(await HistoryNullCountAsync(database, id)).ShouldBe(2, "standing first, not vacuous");
		(await database.ScalarAsync<int>($"SELECT COUNT(*) FROM [{PrincipalTable}] WHERE [Extra] IS NULL", [],
			Cancellation)).ShouldBe(0);
		return (database, id);
	}

	static Task<int> HistoryNullCountAsync(SqlServerDatabase database, int id) =>
		database.ScalarAsync<int>($"SELECT COUNT(*) FROM [dbo].[{HistoryTable}] WHERE [Id] = @p0 AND [Extra] IS NULL",
			[id], Cancellation);

	static Task<bool> IsNullableAsync(SqlServerDatabase database, string table) =>
		database.ScalarAsync<bool>(
			"SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(@p0) AND name = 'Extra'", [table],
			Cancellation);
}
