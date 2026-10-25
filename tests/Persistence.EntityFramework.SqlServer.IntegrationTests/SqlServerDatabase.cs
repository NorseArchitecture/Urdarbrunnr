using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Norse.Persistence.EntityFramework.SqlServer.IntegrationTests;

// CA2100: every command text below is a literal this file or a test declares; every value that varies is bound
// as a parameter.
#pragma warning disable CA2100

/// <summary>
///     One test's database on the shared container, with the two things the lifecycle suites need: schema
///     applied only through <em>generated</em> migrations (EF's real model differ between two built models, then
///     the real generator as <see cref="NorseSqlServerEfProvider" /> wires it, executed by EF's own migration
///     command executor — no hand-written DDL anywhere), and raw catalog and row access for the assertions. The
///     period columns are <c>HIDDEN</c> and outside the EF model by design (spec §6.1), so every period reading
///     here is a deliberate trip past EF rather than a gap in the mapping.
/// </summary>
/// <param name="connectionString">The connection string for this test's database.</param>
sealed class SqlServerDatabase(string connectionString)
{
	/// <summary>A context of the given model over this database, wired exactly as a consumer's would be.</summary>
	public TContext Context<TContext>() where TContext : DbContext =>
		Context<TContext>(stockGenerator: false);

	// stockGenerator swaps SQL Server's own migrations SQL generator back in over the chassis's, so a test can
	// show the engine refusing exactly what the chassis's guards stand in front of.
	TContext Context<TContext>(bool stockGenerator) where TContext : DbContext
	{
		DbContextOptionsBuilder<TContext> optionsBuilder = new();
		optionsBuilder.ApplyNorseProviderOptions(NorseSqlServerEfProvider.Instance, connectionString,
			migrationsAssemblyName: null);
		if (stockGenerator)
			optionsBuilder.ReplaceService<IMigrationsSqlGenerator, SqlServerMigrationsSqlGenerator>();
		return Activator.CreateInstance(typeof(TContext), optionsBuilder.Options) as TContext
			?? throw new InvalidOperationException($"{typeof(TContext).Name} takes its options as its only argument.");
	}

	/// <summary>
	///     Generates the migration from <typeparamref name="TFrom" />'s model to <typeparamref name="TTo" />'s and
	///     applies it to this database — the path <c>dotnet ef migrations add</c> plus <c>Migrate()</c> takes,
	///     minus the scaffolded C# in between.
	/// </summary>
	public Task MigrateAsync<TFrom, TTo>(CancellationToken cancellationToken = default)
		where TFrom : DbContext
		where TTo : DbContext =>
		MigrateAsync<TFrom, TTo>(stockGenerator: false, cancellationToken);

	/// <summary>
	///     <see cref="MigrateAsync{TFrom,TTo}(CancellationToken)" /> with SQL Server's own migrations SQL generator
	///     in place of the chassis's — what the migration would do without the chassis's guards.
	/// </summary>
	public Task MigrateWithStockGeneratorAsync<TFrom, TTo>(CancellationToken cancellationToken = default)
		where TFrom : DbContext
		where TTo : DbContext =>
		MigrateAsync<TFrom, TTo>(stockGenerator: true, cancellationToken);

	async Task MigrateAsync<TFrom, TTo>(bool stockGenerator, CancellationToken cancellationToken)
		where TFrom : DbContext
		where TTo : DbContext
	{
		await using var from = Context<TFrom>();
		await using var to = Context<TTo>(stockGenerator);
		var targetModel = to.GetService<IDesignTimeModel>().Model;
		var operations = to.GetService<IMigrationsModelDiffer>().GetDifferences(
			from.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
			targetModel.GetRelationalModel());
		var commands = to.GetService<IMigrationsSqlGenerator>().Generate(operations, targetModel);
		await to.GetService<IMigrationCommandExecutor>()
			.ExecuteNonQueryAsync(commands, to.GetService<IRelationalConnection>(), cancellationToken);
	}

	/// <summary>
	///     Inserts one user through EF and renames it once, so a closed version sits in history before the
	///     evolution step under test. EF's own write path, so neither statement names a period column.
	/// </summary>
	/// <returns>The user's id; its history row records <paramref name="first" />.</returns>
	public async Task<int> SeedVersionedUserAsync(string first, string second,
		CancellationToken cancellationToken = default)
	{
		await using var context = Context<OptedInContext>();
		PrincipalUser user = new() { Name = first };
		context.Users.Add(user);
		await context.SaveChangesAsync(cancellationToken);
		user.Name = second;
		await context.SaveChangesAsync(cancellationToken);
		return user.Id;
	}

	public async Task ExecuteAsync(string sql, IReadOnlyList<object> arguments,
		CancellationToken cancellationToken = default)
	{
		await using var connection = await OpenAsync(cancellationToken);
		await using var command = Command(connection, sql, arguments);
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	/// <summary>The first column of every row, in order.</summary>
	public async Task<List<T>> ListAsync<T>(string sql, IReadOnlyList<object> arguments,
		CancellationToken cancellationToken = default)
	{
		await using var connection = await OpenAsync(cancellationToken);
		await using var command = Command(connection, sql, arguments);
		await using var reader = await command.ExecuteReaderAsync(cancellationToken);
		List<T> values = [];
		while (await reader.ReadAsync(cancellationToken))
			values.Add(await reader.GetFieldValueAsync<T>(0, cancellationToken));
		return values;
	}

	public async Task<T> ScalarAsync<T>(string sql, IReadOnlyList<object> arguments,
		CancellationToken cancellationToken = default) =>
		(await ListAsync<T>(sql, arguments, cancellationToken)).ShouldHaveSingleItem($"'{sql}' should return one row");

	/// <summary>The server's clock — the one the period columns are stamped from.</summary>
	public Task<DateTime> ServerClockAsync(CancellationToken cancellationToken = default) =>
		ScalarAsync<DateTime>("SELECT SYSUTCDATETIME()", [], cancellationToken);

	/// <summary>
	///     <c>sys.tables.temporal_type</c>: 0 non-temporal, 1 history, 2 system-versioned; <see langword="null" />
	///     when the table does not exist.
	/// </summary>
	public async Task<int?> TemporalTypeAsync(string table, CancellationToken cancellationToken = default) =>
		await ListAsync<int>("SELECT CAST(temporal_type AS int) FROM sys.tables WHERE name = @p0", [table],
			cancellationToken) is [var type] ?
			type :
			null;

	/// <summary>The history table <c>sys.tables</c> associates with <paramref name="table" />, if any.</summary>
	public async Task<string?> HistoryTableOfAsync(string table, CancellationToken cancellationToken = default) =>
		(await ListAsync<string>(
			"""
			SELECT h.name FROM sys.tables t JOIN sys.tables h ON h.object_id = t.history_table_id
			WHERE t.name = @p0
			""", [table], cancellationToken)).SingleOrDefault();

	public async Task<bool> TableExistsAsync(string table, CancellationToken cancellationToken = default) =>
		await TemporalTypeAsync(table, cancellationToken) is not null;

	public Task<List<string>> TablesAsync(CancellationToken cancellationToken = default) =>
		ListAsync<string>("SELECT name FROM sys.tables ORDER BY name", [], cancellationToken);

	/// <summary>Every column of <paramref name="table" />, hidden ones included, in ordinal order.</summary>
	public Task<List<string>> ColumnsAsync(string table, CancellationToken cancellationToken = default) =>
		ListAsync<string>("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@p0) ORDER BY column_id",
			[table], cancellationToken);

	public Task<List<string>> HiddenColumnsAsync(string table, CancellationToken cancellationToken = default) =>
		ListAsync<string>(
			"SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@p0) AND is_hidden = 1 ORDER BY column_id",
			[table], cancellationToken);

	/// <summary><c>sys.columns.max_length</c>, in bytes (an <c>nvarchar(n)</c> column reports 2n).</summary>
	public Task<int> MaxLengthAsync(string table, string column, CancellationToken cancellationToken = default) =>
		ScalarAsync<int>(
			"SELECT CAST(max_length AS int) FROM sys.columns WHERE object_id = OBJECT_ID(@p0) AND name = @p1",
			[table, column], cancellationToken);

	public Task<int> PeriodCountAsync(string table, CancellationToken cancellationToken = default) =>
		ScalarAsync<int>("SELECT COUNT(*) FROM sys.periods WHERE object_id = OBJECT_ID(@p0)", [table],
			cancellationToken);

	public Task<List<string>> DefaultConstraintsAsync(string table, CancellationToken cancellationToken = default) =>
		ListAsync<string>(
			"SELECT name FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(@p0) ORDER BY name",
			[table], cancellationToken);

	/// <summary>The recorded <c>Name</c> of every closed version of one user, oldest first.</summary>
	public Task<List<string>> HistoryNamesAsync(int id, CancellationToken cancellationToken = default) =>
		ListAsync<string>(
			$"""
			 SELECT [Name] FROM [dbo].[{PrincipalTableOnlySchema.HistoryTable}]
			 WHERE [Id] = @p0 ORDER BY [{PrincipalTableOnlySchema.PeriodStart}]
			 """, [id], cancellationToken);

	async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
	{
		SqlConnection connection = new(connectionString);
		await connection.OpenAsync(cancellationToken);
		return connection;
	}

	static SqlCommand Command(SqlConnection connection, string sql, IReadOnlyList<object> arguments)
	{
		SqlCommand command = new(sql, connection);
		command.Parameters.AddRange(
			[.. arguments.Select((argument, index) => new SqlParameter($"@p{index}", argument))]);
		return command;
	}
}
