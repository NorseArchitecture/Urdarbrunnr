using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework.SqlServer.IntegrationTests;

/// <summary>
///     <c>HasFragmentCheckConstraint</c> applied to a real SQL Server 2025. Schema arrives only through generated
///     migrations (<see cref="SqlServerDatabase.MigrateAsync{TFrom,TTo}(CancellationToken)" />), and every assertion
///     reads the catalog or the engine's own refusal: the constraint is a <c>sys.check_constraints</c> row whose
///     <c>parent_object_id</c> is the fragment, trusted and enabled, and it rejects a direct write that violates it
///     while admitting one that does not — including on the never-versioned fragment of a
///     <c>TemporalOnPrincipalTableOnly()</c> entity.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(SqlServerCollection.Name)]
public sealed class FragmentCheckConstraintLiveTests(SqlServerContainerFixture fixture)
{
	const string SkipReason = "Requires a running Docker daemon on a host other than Windows on ARM.";

	// The engine's refusal of a write that conflicts with a constraint (CHECK, FK, …).
	const int ConstraintConflict = 547;

	const int NonTemporal = 0;

	const int SystemVersioned = 2;

	public static bool IsSqlServerTestable => SqlServerContainerFixture.IsTestable;

	static CancellationToken Cancellation => TestContext.Current.CancellationToken;

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task A_created_fragment_carries_the_constraint_and_the_engine_enforces_it()
	{
		var database = await CreateAsync("fragment_check_create");
		await database.MigrateAsync<NoAccountsContext, CheckedAccountsContext>(Cancellation);

		(await ConstrainedTablesAsync(database)).ShouldBe([FragmentCheck.FragmentTable]);
		(await TrustedAsync(database)).ShouldBeTrue("an enabled constraint the engine validated");
		await ShouldEnforceAsync(database);
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task The_unversioned_fragment_of_a_principal_table_only_entity_enforces_the_constraint()
	{
		var database = await CreateAsync("fragment_check_principal_table_only");
		await database.MigrateAsync<NoAccountsContext, PrincipalTableOnlyCheckedAccountsContext>(Cancellation);

		(await database.TemporalTypeAsync(FragmentCheck.PrincipalTable, Cancellation)).ShouldBe(SystemVersioned);
		(await database.TemporalTypeAsync(FragmentCheck.FragmentTable, Cancellation)).ShouldBe(NonTemporal);
		(await ConstrainedTablesAsync(database)).ShouldBe([FragmentCheck.FragmentTable]);
		await ShouldEnforceAsync(database);
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Adding_then_removing_the_constraint_on_an_existing_fragment_applies_both_ways()
	{
		var database = await CreateAsync("fragment_check_add_drop");
		await database.MigrateAsync<NoAccountsContext, UncheckedAccountsContext>(Cancellation);
		// A conforming row already in place: ADD CONSTRAINT validates it and keeps it.
		await InsertCredentialsAsync(database, await InsertPrincipalAsync(database), "A", "A");

		await database.MigrateAsync<UncheckedAccountsContext, CheckedAccountsContext>(Cancellation);
		(await ConstrainedTablesAsync(database)).ShouldBe([FragmentCheck.FragmentTable]);
		(await TrustedAsync(database)).ShouldBeTrue();
		await ShouldEnforceAsync(database);

		await database.MigrateAsync<CheckedAccountsContext, UncheckedAccountsContext>(Cancellation);
		(await ConstrainedTablesAsync(database)).ShouldBeEmpty();
		await InsertCredentialsAsync(database, await InsertPrincipalAsync(database), "A", "B");
	}

	[Fact(SkipUnless = nameof(IsSqlServerTestable), Skip = SkipReason)]
	async Task Adding_the_constraint_over_a_violating_row_is_refused_by_the_engine()
	{
		// Loud at apply time, never silent: ADD CONSTRAINT validates what is already there.
		var database = await CreateAsync("fragment_check_add_violating");
		await database.MigrateAsync<NoAccountsContext, UncheckedAccountsContext>(Cancellation);
		await InsertCredentialsAsync(database, await InsertPrincipalAsync(database), "A", "B");

		var refusal = await Should.ThrowAsync<SqlException>(() =>
			database.MigrateAsync<UncheckedAccountsContext, CheckedAccountsContext>(Cancellation));

		refusal.Number.ShouldBe(ConstraintConflict, refusal.Message);
		(await ConstrainedTablesAsync(database)).ShouldBeEmpty();
	}

	async Task<SqlServerDatabase> CreateAsync(string name)
	{
		Assert.SkipUnless(fixture.Available, fixture.UnavailableReason ?? SkipReason);
		return new(await fixture.CreateDatabaseAsync(name, Cancellation));
	}

	// A conforming direct insert lands; a violating insert and a violating update are refused, naming the constraint.
	static async Task ShouldEnforceAsync(SqlServerDatabase database)
	{
		var id = await InsertPrincipalAsync(database);
		var insert = await Should.ThrowAsync<SqlException>(() => InsertCredentialsAsync(database, id, "A", "B"));
		insert.Number.ShouldBe(ConstraintConflict, insert.Message);
		insert.Message.ShouldContain(FragmentCheck.Constraint);

		await InsertCredentialsAsync(database, id, "A", "A");

		var update = await Should.ThrowAsync<SqlException>(() => database.ExecuteAsync(
			$"UPDATE [{FragmentCheck.FragmentTable}] SET [NormalizedEmail] = N'C' WHERE [Id] = @p0", [id], Cancellation));
		update.Number.ShouldBe(ConstraintConflict, update.Message);
		update.Message.ShouldContain(FragmentCheck.Constraint);
		await database.ExecuteAsync(
			$"UPDATE [{FragmentCheck.FragmentTable}] SET [NormalizedEmail] = N'C', [NormalizedUserName] = N'C' WHERE [Id] = @p0",
			[id], Cancellation);
	}

	static Task<int> InsertPrincipalAsync(SqlServerDatabase database) =>
		database.ScalarAsync<int>(
			$"INSERT INTO [{FragmentCheck.PrincipalTable}] ([Name]) OUTPUT INSERTED.[Id] VALUES (N'a')", [], Cancellation);

	static Task InsertCredentialsAsync(SqlServerDatabase database, int id, string email, string userName) =>
		database.ExecuteAsync(
			$"INSERT INTO [{FragmentCheck.FragmentTable}] ([Id], [NormalizedEmail], [NormalizedUserName]) VALUES (@p0, @p1, @p2)",
			[id, email, userName], Cancellation);

	// Every table carrying a check constraint of this name.
	static Task<List<string>> ConstrainedTablesAsync(SqlServerDatabase database) =>
		database.ListAsync<string>(
			"SELECT OBJECT_NAME(parent_object_id) FROM sys.check_constraints WHERE name = @p0 ORDER BY 1",
			[FragmentCheck.Constraint], Cancellation);

	static Task<bool> TrustedAsync(SqlServerDatabase database) =>
		database.ScalarAsync<bool>(
			"SELECT CAST(CASE WHEN is_disabled = 0 AND is_not_trusted = 0 THEN 1 ELSE 0 END AS bit) FROM sys.check_constraints WHERE name = @p0",
			[FragmentCheck.Constraint], Cancellation);
}

/// <summary>The names and SQL the fragment check-constraint suite maps.</summary>
static class FragmentCheck
{
	public const string PrincipalTable = "Accounts";

	public const string FragmentTable = "AccountCredentials";

	public const string Constraint = "CK_AccountCredentials_EmailIsUserName";

	public const string Sql = "[NormalizedEmail] = [NormalizedUserName]";
}

sealed class NoAccountsContext(DbContextOptions<NoAccountsContext> options) : NorseDbContext(options);

// Every variant maps the same table pair, Accounts split to AccountCredentials.
abstract class AccountsContext<TAccount>(DbContextOptions options) : NorseDbContext(options)
	where TAccount : AccountShape, INorseEntity<TAccount>
{
	protected virtual void Credentials(SplitTableBuilder<TAccount> credentials) { }

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		var account = builder.Entity<TAccount>();
		TAccount.Configure(account);
		account
			.ToTable(FragmentCheck.PrincipalTable)
			.SplitToTable(FragmentCheck.FragmentTable, credentials =>
			{
				credentials.Property(entity => entity.NormalizedEmail);
				credentials.Property(entity => entity.NormalizedUserName);
				Credentials(credentials);
			});
	}
}

sealed class UncheckedAccountsContext(DbContextOptions<UncheckedAccountsContext> options)
	: AccountsContext<Account>(options);

sealed class CheckedAccountsContext(DbContextOptions<CheckedAccountsContext> options)
	: AccountsContext<Account>(options)
{
	protected override void Credentials(SplitTableBuilder<Account> credentials) =>
		credentials.HasFragmentCheckConstraint(FragmentCheck.Constraint, FragmentCheck.Sql);
}

sealed class PrincipalTableOnlyCheckedAccountsContext(DbContextOptions<PrincipalTableOnlyCheckedAccountsContext> options)
	: AccountsContext<PrincipalTableOnlyAccount>(options)
{
	protected override void Credentials(SplitTableBuilder<PrincipalTableOnlyAccount> credentials) =>
		credentials.HasFragmentCheckConstraint(FragmentCheck.Constraint, FragmentCheck.Sql);
}

abstract class AccountShape
{
	public int Id { get; set; }

	[MaxLength(100)] public string Name { get; set; } = "";

	[MaxLength(256)] public string NormalizedEmail { get; set; } = "";

	[MaxLength(256)] public string NormalizedUserName { get; set; } = "";
}

sealed class Account : AccountShape, INorseEntity<Account>
{
	public static void Configure(EntityTypeBuilder<Account> builder) { }
}

sealed class PrincipalTableOnlyAccount : AccountShape, ITemporalEntity, INorseEntity<PrincipalTableOnlyAccount>
{
	public static void Configure(EntityTypeBuilder<PrincipalTableOnlyAccount> builder) =>
		builder.TemporalOnPrincipalTableOnly();
}
