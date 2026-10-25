using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using static Norse.Persistence.EntityFramework.PostgreSQL.Tests.FragmentChecks;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

/// <summary>
///     <c>HasFragmentCheckConstraint</c> applied to a real <c>postgres:19rc1</c> server. Schema arrives only through
///     generated migrations — EF's real differ between two built models, then the real generator, executed by EF's
///     own migration command executor — and every assertion reads the catalog or the engine's own refusal: the
///     constraint is a <c>pg_constraint</c> row of type <c>c</c> on the fragment's relation, and it rejects a
///     direct write that violates it while admitting one that does not.
/// </summary>
/// <param name="fixture">The shared container.</param>
[Collection(PostgresCollection.Name)]
public sealed class FragmentCheckConstraintLiveTests(PostgresContainerFixture fixture)
{
	static CancellationToken Cancellation => TestContext.Current.CancellationToken;

	[Fact]
	async Task A_created_fragment_carries_the_constraint_and_the_engine_enforces_it()
	{
		var connectionString = await fixture.CreateDatabaseAsync("fragment_check_create", Cancellation);
		await MigrateAsync<NoAccountsContext, CheckedAccountsContext>(connectionString);

		(await ConstrainedRelationsAsync(connectionString)).ShouldBe([FragmentTable]);
		(await DefinitionAsync(connectionString)).ShouldBe($"CHECK (((normalized_email)::text = (normalized_user_name)::text))");
		await ShouldEnforceAsync(connectionString);
	}

	[Fact]
	async Task The_fragment_of_a_temporal_entity_enforces_the_constraint_beside_the_apparatus()
	{
		var connectionString = await fixture.CreateDatabaseAsync("fragment_check_temporal", Cancellation);
		await MigrateAsync<NoAccountsContext, TemporalCheckedAccountsContext>(connectionString);

		(await ConstrainedRelationsAsync(connectionString)).ShouldBe([FragmentTable]);
		await ShouldEnforceAsync(connectionString);
	}

	[Fact]
	async Task Adding_then_removing_the_constraint_on_an_existing_fragment_applies_both_ways()
	{
		var connectionString = await fixture.CreateDatabaseAsync("fragment_check_add_drop", Cancellation);
		await MigrateAsync<NoAccountsContext, UncheckedAccountsContext>(connectionString);
		var before = await InsertPrincipalAsync(connectionString);
		// A conforming row already in place: ADD CONSTRAINT validates it and keeps it.
		await ExecuteAsync(connectionString,
			$"INSERT INTO {FragmentTable} (id, normalized_email, normalized_user_name) VALUES ($1, 'A', 'A')", before);

		await MigrateAsync<UncheckedAccountsContext, CheckedAccountsContext>(connectionString);
		(await ConstrainedRelationsAsync(connectionString)).ShouldBe([FragmentTable]);
		await ShouldEnforceAsync(connectionString);

		await MigrateAsync<CheckedAccountsContext, UncheckedAccountsContext>(connectionString);
		(await ConstrainedRelationsAsync(connectionString)).ShouldBeEmpty();
		var after = await InsertPrincipalAsync(connectionString);
		await ExecuteAsync(connectionString,
			$"INSERT INTO {FragmentTable} (id, normalized_email, normalized_user_name) VALUES ($1, 'A', 'B')", after);
	}

	[Fact]
	async Task Adding_the_constraint_over_a_violating_row_is_refused_by_the_engine()
	{
		// Loud at apply time, never silent: ADD CONSTRAINT validates what is already there.
		var connectionString = await fixture.CreateDatabaseAsync("fragment_check_add_violating", Cancellation);
		await MigrateAsync<NoAccountsContext, UncheckedAccountsContext>(connectionString);
		var id = await InsertPrincipalAsync(connectionString);
		await ExecuteAsync(connectionString,
			$"INSERT INTO {FragmentTable} (id, normalized_email, normalized_user_name) VALUES ($1, 'A', 'B')", id);

		var refusal = await Should.ThrowAsync<PostgresException>(() =>
			MigrateAsync<UncheckedAccountsContext, CheckedAccountsContext>(connectionString));

		refusal.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
		(await ConstrainedRelationsAsync(connectionString)).ShouldBeEmpty();
	}

	// A conforming direct insert lands; a violating insert and a violating update are refused by name.
	static async Task ShouldEnforceAsync(string connectionString)
	{
		var id = await InsertPrincipalAsync(connectionString);
		var insert = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connectionString,
			$"INSERT INTO {FragmentTable} (id, normalized_email, normalized_user_name) VALUES ($1, 'A', 'B')", id));
		insert.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
		insert.ConstraintName.ShouldBe(Constraint);

		await ExecuteAsync(connectionString,
			$"INSERT INTO {FragmentTable} (id, normalized_email, normalized_user_name) VALUES ($1, 'A', 'A')", id);

		var update = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connectionString,
			$"UPDATE {FragmentTable} SET normalized_email = 'C' WHERE id = $1", id));
		update.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
		update.ConstraintName.ShouldBe(Constraint);
		await ExecuteAsync(connectionString,
			$"UPDATE {FragmentTable} SET normalized_email = 'C', normalized_user_name = 'C' WHERE id = $1", id);
	}

	static async Task MigrateAsync<TFrom, TTo>(string connectionString)
		where TFrom : DbContext
		where TTo : DbContext
	{
		await using var from = Create<TFrom>(connectionString);
		await using var to = Create<TTo>(connectionString);
		var targetModel = to.GetService<IDesignTimeModel>().Model;
		var commands = to.GetService<IMigrationsSqlGenerator>()
			.Generate(TemporalEvolution.Operations(from, to), targetModel);
		await to.GetService<IMigrationCommandExecutor>()
			.ExecuteNonQueryAsync(commands, to.GetService<IRelationalConnection>(), Cancellation);
	}

	static TContext Create<TContext>(string connectionString) where TContext : DbContext =>
		Activator.CreateInstance(typeof(TContext), TemporalEvolution.LiveOptions<TContext>(connectionString)) as TContext
		?? throw new InvalidOperationException($"{typeof(TContext).Name} takes its options as its only argument.");

	// Every relation carrying a check constraint of this name.
	static async Task<List<string>> ConstrainedRelationsAsync(string connectionString) =>
		await ListAsync<string>(connectionString,
			"""
			SELECT c.relname::text FROM pg_catalog.pg_constraint k
			JOIN pg_catalog.pg_class c ON c.oid = k.conrelid
			WHERE k.contype = 'c' AND k.conname = $1 ORDER BY c.relname
			""", Constraint);

	static async Task<string> DefinitionAsync(string connectionString) =>
		(await ListAsync<string>(connectionString,
			"SELECT pg_catalog.pg_get_constraintdef(oid) FROM pg_catalog.pg_constraint WHERE conname = $1",
			Constraint)).ShouldHaveSingleItem();

	static async Task<int> InsertPrincipalAsync(string connectionString) =>
		(await ListAsync<int>(connectionString, $"INSERT INTO {PrincipalTable} (name) VALUES ('a') RETURNING id"))
		.ShouldHaveSingleItem();

	static async Task ExecuteAsync(string connectionString, string sql, params object[] arguments)
	{
		await using NpgsqlConnection connection = new(connectionString);
		await connection.OpenAsync(Cancellation);
		await TemporalSql.ExecuteAsync(connection, sql, arguments);
	}

	static async Task<List<T>> ListAsync<T>(string connectionString, string sql, params object[] arguments)
	{
		await using NpgsqlConnection connection = new(connectionString);
		await connection.OpenAsync(Cancellation);
		await using var command = TemporalSql.Command(connection, sql, arguments);
		await using var reader = await command.ExecuteReaderAsync(Cancellation);
		List<T> values = [];
		while (await reader.ReadAsync(Cancellation))
			values.Add(await reader.GetFieldValueAsync<T>(0, Cancellation));
		return values;
	}
}
