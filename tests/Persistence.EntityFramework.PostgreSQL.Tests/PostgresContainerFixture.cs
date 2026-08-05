using Npgsql;
using Testcontainers.PostgreSql;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

// CA2100: the only value reaching this command text is a database name the tests in this assembly
// supply as literals, and a database name cannot be a parameter in CREATE DATABASE regardless.
#pragma warning disable CA2100

/// <summary>
/// One PostgreSQL 19beta2 container — the same image Bifröst's AppHost runs — shared by every test in
/// the <see cref="PostgresCollection"/>, with a database of its own per test. The temporal apparatus is
/// database semantics end to end (a <c>WITHOUT OVERLAPS</c> key, a <c>SECURITY DEFINER</c> trigger, a
/// view over two tables), so it is proved against a real server or not at all.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
	readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:19beta2")
		.WithDatabase("norse_test")
		.Build();

	// null! justified: hydrated by InitializeAsync before xUnit hands the fixture to any test.
	string _connectionString = null!;

	public async ValueTask InitializeAsync()
	{
		await _container.StartAsync();
		_connectionString = _container.GetConnectionString();
	}

	/// <summary>
	/// Creates a database for one test and returns its connection string. Per-test rather than per-run:
	/// these tests build and then rebuild the same apparatus, so shared state would let one test's
	/// leftovers decide another's result.
	/// </summary>
	/// <param name="name">The database name — one per test, and its own documentation in <c>\l</c>.</param>
	/// <param name="cancellationToken">The test's cancellation token.</param>
	public async Task<string> CreateDatabaseAsync(string name, CancellationToken cancellationToken)
	{
		await using NpgsqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);
		await using NpgsqlCommand command = new($"CREATE DATABASE \"{name}\"", connection);
		await command.ExecuteNonQueryAsync(cancellationToken);
		return new NpgsqlConnectionStringBuilder(_connectionString) { Database = name }.ConnectionString;
	}

	public ValueTask DisposeAsync() =>
		_container.DisposeAsync();
}
