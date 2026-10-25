using static Norse.Persistence.EntityFramework.SqlServer.IntegrationTests.PrincipalTableOnlySchema;

namespace Norse.Persistence.EntityFramework.SqlServer.IntegrationTests;

/// <summary>
///     Every supported row of spec §6.2's lifecycle table, applied to a live SQL Server 2025 through generated
///     migrations — EF's real differ between two models, then the real generator — with a closed version
///     written to history before each evolution step and read back after it. The snapshot suite
///     (<c>PrincipalTableOnlyMigrationSqlTests</c>) proves the SQL says what the spec says; this one proves the
///     engine accepts it and that the history association survives it. Assertions read the catalog
///     (<c>sys.tables.temporal_type</c>, <c>sys.periods</c>, <c>sys.columns</c>, <c>sys.default_constraints</c>),
///     never a migration name.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(SqlServerCollection.Name)]
public sealed class PrincipalTableOnlyLifecycleTests(SqlServerContainerFixture fixture)
{
	const string SkipReason = "Requires a running Docker daemon on a host other than Windows on ARM.";

	const int SystemVersioned = 2;

	const int History = 1;

	const int NonTemporal = 0;

	public static bool IsSqlServerTestable => SqlServerContainerFixture.IsTestable;

	static CancellationToken Cancellation => TestContext.Current.CancellationToken;

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Create_versions_the_principal_table_alone_with_hidden_periods()
	{
		var database = await CreateAsync("lifecycle_create");

		await database.MigrateAsync<EmptyContext, OptedInContext>(Cancellation);

		(await database.TemporalTypeAsync(PrincipalTable, Cancellation)).ShouldBe(SystemVersioned);
		(await database.HistoryTableOfAsync(PrincipalTable, Cancellation)).ShouldBe(HistoryTable);
		(await database.TemporalTypeAsync(HistoryTable, Cancellation)).ShouldBe(History);
		(await database.PeriodCountAsync(PrincipalTable, Cancellation)).ShouldBe(1);
		(await database.HiddenColumnsAsync(PrincipalTable, Cancellation)).ShouldBe([PeriodStart, PeriodEnd]);
		// Created periods need no defaults (the engine fills them), so there are none to clean up later.
		(await database.DefaultConstraintsAsync(PrincipalTable, Cancellation)).ShouldBeEmpty();
		(await database.TemporalTypeAsync(FragmentTable, Cancellation)).ShouldBe(NonTemporal);
		(await database.ColumnsAsync(FragmentTable, Cancellation)).ShouldNotContain(PeriodStart);
		var id = await database.SeedVersionedUserAsync("v1", "v2", Cancellation);
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1"]);
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Adding_a_principal_column_propagates_to_history_with_versioning_on()
	{
		var database = await CreateOptedInAsync("lifecycle_add_column");
		var id = await database.SeedVersionedUserAsync("v1", "v2", Cancellation);

		await database.MigrateAsync<OptedInContext, AddedColumnContext>(Cancellation);

		(await database.TemporalTypeAsync(PrincipalTable, Cancellation)).ShouldBe(SystemVersioned);
		(await database.ColumnsAsync(HistoryTable, Cancellation)).ShouldContain("Extra");
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1"], "the pre-migration version survives");
		await database.ExecuteAsync($"UPDATE [{PrincipalTable}] SET [Extra] = 'x' WHERE [Id] = @p0", [id],
			Cancellation);
		(await database.ScalarAsync<int>(
			$"SELECT COUNT(*) FROM [dbo].[{HistoryTable}] WHERE [Id] = @p0 AND [Extra] IS NULL", [id],
			Cancellation)).ShouldBe(2, "both closed versions predate the column and honestly say NULL");
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Dropping_a_principal_column_propagates_to_history_with_versioning_on()
	{
		var database = await CreateAsync("lifecycle_drop_column");
		await database.MigrateAsync<EmptyContext, AddedColumnContext>(Cancellation);
		var id = await database.SeedVersionedUserAsync("v1", "v2", Cancellation);
		await database.ExecuteAsync($"UPDATE [{PrincipalTable}] SET [Extra] = 'x' WHERE [Id] = @p0", [id],
			Cancellation);

		await database.MigrateAsync<AddedColumnContext, OptedInContext>(Cancellation);

		(await database.TemporalTypeAsync(PrincipalTable, Cancellation)).ShouldBe(SystemVersioned);
		(await database.ColumnsAsync(PrincipalTable, Cancellation)).ShouldNotContain("Extra");
		(await database.ColumnsAsync(HistoryTable, Cancellation)).ShouldNotContain("Extra");
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1", "v2"]);
		await UpdateNameAsync(database, id, "v3");
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1", "v2", "v2"]);
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Widening_a_principal_column_propagates_to_history_with_versioning_on()
	{
		var database = await CreateOptedInAsync("lifecycle_alter_column");
		var id = await database.SeedVersionedUserAsync("v1", "v2", Cancellation);

		await database.MigrateAsync<OptedInContext, WidenedColumnContext>(Cancellation);

		(await database.TemporalTypeAsync(PrincipalTable, Cancellation)).ShouldBe(SystemVersioned);
		// nvarchar(200): 400 bytes, on both sides of the association (G5: HIDDEN-period tables propagate too).
		(await database.MaxLengthAsync(PrincipalTable, "Name", Cancellation)).ShouldBe(400);
		(await database.MaxLengthAsync(HistoryTable, "Name", Cancellation)).ShouldBe(400);
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1"]);
		var wide = new string('w', 150);
		await UpdateNameAsync(database, id, wide);
		await UpdateNameAsync(database, id, "v4");
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1", "v2", wide]);
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task A_fragment_column_change_is_plain_DDL_and_leaves_principal_history_alone()
	{
		var database = await CreateOptedInAsync("lifecycle_fragment_column");
		var id = await database.SeedVersionedUserAsync("v1", "v2", Cancellation);

		await database.MigrateAsync<OptedInContext, FragmentColumnContext>(Cancellation);

		(await database.ColumnsAsync(FragmentTable, Cancellation)).ShouldContain("Note");
		(await database.TemporalTypeAsync(FragmentTable, Cancellation)).ShouldBe(NonTemporal);
		(await database.ColumnsAsync(HistoryTable, Cancellation)).ShouldNotContain("Note");
		(await database.TemporalTypeAsync(PrincipalTable, Cancellation)).ShouldBe(SystemVersioned);
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1"]);
		await database.ExecuteAsync($"UPDATE [{FragmentTable}] SET [Note] = 'n' WHERE [Id] = @p0", [id],
			Cancellation);
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1"], "the fragment never versions");
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Enable_versions_an_existing_principal_table_through_named_period_defaults()
	{
		var database = await CreatePlainAsync("lifecycle_enable");
		var id = await InsertPlainAsync(database, "v1");

		await database.MigrateAsync<PlainContext, OptedInContext>(Cancellation);

		(await database.TemporalTypeAsync(PrincipalTable, Cancellation)).ShouldBe(SystemVersioned);
		(await database.HistoryTableOfAsync(PrincipalTable, Cancellation)).ShouldBe(HistoryTable);
		(await database.PeriodCountAsync(PrincipalTable, Cancellation)).ShouldBe(1);
		(await database.HiddenColumnsAsync(PrincipalTable, Cancellation)).ShouldBe([PeriodStart, PeriodEnd]);
		(await database.DefaultConstraintsAsync(PrincipalTable, Cancellation)).ShouldBe(
			["DF_Users_SystemPeriodEnd", "DF_Users_SystemPeriodStart"]);
		(await database.TemporalTypeAsync(FragmentTable, Cancellation)).ShouldBe(NonTemporal);
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBeEmpty(
			"the table's pre-temporal past is honestly unrecorded");
		await UpdateNameAsync(database, id, "v2");
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1"]);
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Enable_on_populated_table_opens_existing_rows_at_enable_time()
	{
		var database = await CreatePlainAsync("lifecycle_enable_populated");
		foreach (var name in (string[])["a", "b", "c"])
			await InsertPlainAsync(database, name);
		var before = await database.ServerClockAsync(Cancellation);

		await database.MigrateAsync<PlainContext, OptedInContext>(Cancellation);

		var after = await database.ServerClockAsync(Cancellation);
		// SYSUTCDATETIME() is evaluated once per statement, so every pre-existing row opens at one instant —
		// and that instant is the enable, read off the server's own clock on both sides.
		var opened = (await database.ListAsync<DateTime>($"SELECT [{PeriodStart}] FROM [{PrincipalTable}]", [],
			Cancellation)).Distinct().ShouldHaveSingleItem("one enable timestamp stamps every pre-existing row");
		opened.ShouldBeInRange(before, after);
		(await database.ScalarAsync<int>(
			$"""
			 SELECT COUNT(*) FROM [{PrincipalTable}]
			 WHERE [{PeriodEnd}] = CONVERT(datetime2, '9999-12-31 23:59:59.9999999')
			 """, [], Cancellation)).ShouldBe(3, "every pre-existing row enters the timeline as a current version");
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Disable_removes_versioning_the_periods_and_history_and_keeps_the_rows()
	{
		var database = await CreateOptedInAsync("lifecycle_disable");
		var id = await database.SeedVersionedUserAsync("v1", "v2", Cancellation);
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1"], "standing first, not vacuous");

		await database.MigrateAsync<OptedInContext, PlainContext>(Cancellation);

		await ShouldBePlainAsync(database);
		(await database.ScalarAsync<string>($"SELECT [Name] FROM [{PrincipalTable}] WHERE [Id] = @p0", [id],
			Cancellation)).ShouldBe("v2");
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Disable_after_enable_drops_the_period_defaults()
	{
		// An enabled (not created) table carries the enable's named defaults, and DROP COLUMN fails on them with
		// Msg 5074 unless the disable drops exactly those names first.
		var database = await CreatePlainAsync("lifecycle_disable_after_enable");
		var id = await InsertPlainAsync(database, "v1");
		await database.MigrateAsync<PlainContext, OptedInContext>(Cancellation);
		await UpdateNameAsync(database, id, "v2");
		(await database.DefaultConstraintsAsync(PrincipalTable, Cancellation)).Count.ShouldBe(2,
			"standing first, not vacuous");

		await database.MigrateAsync<OptedInContext, PlainContext>(Cancellation);

		await ShouldBePlainAsync(database);
		(await database.DefaultConstraintsAsync(PrincipalTable, Cancellation)).ShouldBeEmpty();
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Drop_removes_the_table_its_history_and_the_fragment()
	{
		var database = await CreateOptedInAsync("lifecycle_drop");
		var id = await database.SeedVersionedUserAsync("v1", "v2", Cancellation);
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1"], "standing first, not vacuous");

		await database.MigrateAsync<OptedInContext, EmptyContext>(Cancellation);

		(await database.TablesAsync(Cancellation)).ShouldBeEmpty();
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Disable_then_drop_tears_down_cleanly()
	{
		var database = await CreateOptedInAsync("lifecycle_disable_then_drop");
		var id = await database.SeedVersionedUserAsync("v1", "v2", Cancellation);
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["v1"], "standing first, not vacuous");

		await database.MigrateAsync<OptedInContext, PlainContext>(Cancellation);
		await database.MigrateAsync<PlainContext, EmptyContext>(Cancellation);

		(await database.TablesAsync(Cancellation)).ShouldBeEmpty();
		(await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.periods", [], Cancellation)).ShouldBe(0);
		(await database.ScalarAsync<int>("SELECT COUNT(*) FROM sys.default_constraints", [], Cancellation))
			.ShouldBe(0);
	}

	static async Task ShouldBePlainAsync(SqlServerDatabase database)
	{
		(await database.TemporalTypeAsync(PrincipalTable, Cancellation)).ShouldBe(NonTemporal);
		(await database.TableExistsAsync(HistoryTable, Cancellation)).ShouldBeFalse();
		(await database.PeriodCountAsync(PrincipalTable, Cancellation)).ShouldBe(0);
		// After DROP PERIOD the period columns would be visible, so they must be gone, not merely unhidden.
		(await database.ColumnsAsync(PrincipalTable, Cancellation)).ShouldBe(["Id", "Name"]);
	}

	static async Task UpdateNameAsync(SqlServerDatabase database, int id, string name) =>
		await database.ExecuteAsync($"UPDATE [{PrincipalTable}] SET [Name] = @p1 WHERE [Id] = @p0", [id, name],
			Cancellation);

	// Through the plain model's EF write path: the rows a brownfield table already holds before the enable.
	static async Task<int> InsertPlainAsync(SqlServerDatabase database, string name)
	{
		await using var context = database.Context<PlainContext>();
		PlainUser user = new() { Name = name };
		context.Users.Add(user);
		await context.SaveChangesAsync(Cancellation);
		return user.Id;
	}

	async Task<SqlServerDatabase> CreateAsync(string name)
	{
		Assert.SkipUnless(fixture.Available, fixture.UnavailableReason ?? SkipReason);
		return new(await fixture.CreateDatabaseAsync(name, Cancellation));
	}

	async Task<SqlServerDatabase> CreateOptedInAsync(string name)
	{
		var database = await CreateAsync(name);
		await database.MigrateAsync<EmptyContext, OptedInContext>(Cancellation);
		return database;
	}

	async Task<SqlServerDatabase> CreatePlainAsync(string name)
	{
		var database = await CreateAsync(name);
		await database.MigrateAsync<EmptyContext, PlainContext>(Cancellation);
		return database;
	}
}
