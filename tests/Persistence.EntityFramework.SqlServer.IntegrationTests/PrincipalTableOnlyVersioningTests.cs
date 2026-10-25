using static Norse.Persistence.EntityFramework.SqlServer.IntegrationTests.PrincipalTableOnlySchema;

namespace Norse.Persistence.EntityFramework.SqlServer.IntegrationTests;

/// <summary>
///     What engine-native versioning <em>does</em> on a <c>TemporalOnPrincipalTableOnly()</c> entity once rows
///     move through EF's real split write path (spec §6.1): the principal table versions, the fragment never
///     does, and — unlike the PostgreSQL apparatus — SQL Server records a version for every UPDATE, changed
///     values or not. The schema arrives through a generated migration from the empty model.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(SqlServerCollection.Name)]
public sealed class PrincipalTableOnlyVersioningTests(SqlServerContainerFixture fixture)
{
	const string SkipReason = "Requires a running Docker daemon on a host other than Windows on ARM.";

	public static bool IsSqlServerTestable => SqlServerContainerFixture.IsTestable;

	static CancellationToken Cancellation => TestContext.Current.CancellationToken;

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Engine_is_SQL_Server_2025_or_later()
	{
		Assert.SkipUnless(fixture.Available, fixture.UnavailableReason ?? SkipReason);

		(await fixture.EngineMajorVersionAsync(Cancellation)).ShouldBeGreaterThanOrEqualTo(17,
			"the platform floor is SQL Server 2025 (spec §6.2)");
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Fragment_only_update_writes_no_history_row()
	{
		var database = await CreateAsync("versioning_fragment_only");
		var id = await InsertAsync(database, "stable");

		await WriteAsync(database,
			new() { Id = id, LockoutEnd = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero) },
			nameof(PrincipalUser.LockoutEnd));

		(await database.ScalarAsync<int>($"SELECT COUNT(*) FROM [{FragmentTable}] WHERE [LockoutEnd] IS NOT NULL",
			[], Cancellation)).ShouldBe(1, "the fragment update landed");
		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBeEmpty(
			"the fragment is not versioned and the principal row was never touched");
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Principal_update_writes_one_history_row()
	{
		var database = await CreateAsync("versioning_principal");
		var id = await InsertAsync(database, "before");

		await WriteAsync(database, new() { Id = id, Name = "after" }, nameof(PrincipalUser.Name));

		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["before"]);
		// The closed version ends where the current one begins: the engine's own gapless arithmetic.
		(await database.ScalarAsync<int>(
			$"""
			 SELECT COUNT(*) FROM [{PrincipalTable}] u JOIN [dbo].[{HistoryTable}] h ON h.[Id] = u.[Id]
			 WHERE u.[Id] = @p0 AND h.[{PeriodEnd}] = u.[{PeriodStart}]
			 """, [id], Cancellation)).ShouldBe(1);
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Same_value_update_writes_a_history_row()
	{
		// Engine-native versioning has no no-op suppression: an UPDATE is a new version whatever it wrote. EF
		// never issues one for an unchanged entity, so the statement is raw — the shape a hand-written UPDATE
		// or a stored procedure produces.
		var database = await CreateAsync("versioning_same_value");
		var id = await InsertAsync(database, "unchanged");

		await database.ExecuteAsync($"UPDATE [{PrincipalTable}] SET [Name] = [Name] WHERE [Id] = @p0", [id],
			Cancellation);

		(await database.HistoryNamesAsync(id, Cancellation)).ShouldBe(["unchanged"]);
	}

	async Task<SqlServerDatabase> CreateAsync(string name)
	{
		Assert.SkipUnless(fixture.Available, fixture.UnavailableReason ?? SkipReason);
		SqlServerDatabase database = new(await fixture.CreateDatabaseAsync(name, Cancellation));
		await database.MigrateAsync<EmptyContext, OptedInContext>(Cancellation);
		return database;
	}

	// An explicit write of one column through EF's split write path — the platform forces NoTracking, so the
	// entity is attached and only the named member is marked modified, and EF updates only the table mapping it.
	static async Task WriteAsync(SqlServerDatabase database, PrincipalUser user, string member)
	{
		await using var context = database.Context<OptedInContext>();
		context.Users.Attach(user).Property(member).IsModified = true;
		await context.SaveChangesAsync(Cancellation);
	}

	// Through EF's split write path: one INSERT per table, neither naming a period column (they are HIDDEN and
	// outside the model, PoC G2), and an insert writes no history.
	static async Task<int> InsertAsync(SqlServerDatabase database, string name)
	{
		await using var context = database.Context<OptedInContext>();
		PrincipalUser user = new() { Name = name };
		context.Users.Add(user);
		await context.SaveChangesAsync(Cancellation);
		(await database.HistoryNamesAsync(user.Id, Cancellation)).ShouldBeEmpty();
		return user.Id;
	}
}
