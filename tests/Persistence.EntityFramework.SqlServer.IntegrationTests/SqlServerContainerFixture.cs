using System.Runtime.InteropServices;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Norse.Persistence.EntityFramework.SqlServer.IntegrationTests;

// CA2100: the only value reaching this command text is a database name the tests in this assembly supply as
// literals, and a database name cannot be a parameter in CREATE DATABASE regardless.
#pragma warning disable CA2100

/// <summary>
///     One SQL Server 2025 container shared by every test in the <see cref="SqlServerCollection" />, with a
///     database of its own per test — the chassis's first SQL Server fixture (spec §6.2, which chassis spec §8
///     had deferred). The shape is Midgard's <c>SqlServerContainerFixture</c>: the same image, the same
///     Windows-on-ARM guard, the same narrow catch. Pinned to 2025 rather than the Testcontainers default:
///     <see cref="NorseSqlServerEfProvider" /> forces compatibility level 170, which only a 2025+ engine honors,
///     and <see cref="InitializeAsync" /> asserts that floor before any test runs.
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
	// SQL Server 2025 reports ProductMajorVersion 17.
	const int EngineFloor = 17;

	readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();

	/// <summary>
	///     SQL Server's Linux image is x86_64-only, so an arm64 host emulates it, and the emulator decides
	///     everything. Windows on ARM runs it under qemu, where the 2025 image segfaults; that host alone is
	///     excluded (plan Global Constraints, ruled 2026-10-25). Apple silicon runs it under Rosetta and is
	///     supported; x86_64 CI needs neither. Checked before <c>StartAsync</c>, so an excluded host never pulls
	///     the multi-gigabyte image only to watch it die. A Mac with Rosetta switched off falls back to qemu and
	///     degrades through the narrow catch below into a skip.
	/// </summary>
	public static bool IsSupportedArchitecture { get; } =
		!(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64);

	/// <summary>The <c>[Fact(SkipUnless = …)]</c> pre-flight gate: a daemon answers, on a supported host.</summary>
	public static bool IsTestable =>
		DockerAvailability.IsAvailable && IsSupportedArchitecture;

	/// <summary>
	///     True once a real, usable container is up. A container that starts creating and then dies cannot be
	///     covered by the pre-flight gate, and a collection fixture's <c>InitializeAsync</c> throwing would fail
	///     every test before its body runs — so every test checks this and skips with
	///     <see cref="UnavailableReason" />.
	/// </summary>
	public bool Available { get; private set; }

	public string? UnavailableReason { get; private set; }

	// null! justified: only ever read by a test gated on Available, which InitializeAsync sets after it.
	string _connectionString = null!;

	public async ValueTask InitializeAsync()
	{
		if (!IsTestable)
		{
			UnavailableReason = IsSupportedArchitecture ?
				"No Docker daemon answered." :
				"SQL Server's Linux image is x86_64-only and segfaults under qemu emulation on Windows on ARM.";
			return;
		}

		try
		{
			await _container.StartAsync();
		}
		// Two named types, never a bare catch (Exception): ContainerNotRunningException is Testcontainers' own
		// signal that the container died before becoming ready (the qemu segfault's shape), and
		// NotSupportedException is what Testcontainers.MsSql's sqlcmd readiness probe throws when the container
		// died before the probe could find the binary. Anything else is a real bug and propagates.
		catch (Exception ex) when (ex is ContainerNotRunningException or NotSupportedException)
		{
			UnavailableReason = ex.Message;
			return;
		}

		_connectionString = _container.GetConnectionString();
		// The engine floor (spec §6.2), asserted at startup: a pre-2025 engine would accept most DDL and
		// then fail the native json type far from here.
		var majorVersion = await EngineMajorVersionAsync();
		if (majorVersion < EngineFloor)
			throw new InvalidOperationException(
				$"SQL Server {majorVersion} is below the SQL Server 2025 floor (major version {EngineFloor}).");
		Available = true;
	}

	/// <summary><c>SERVERPROPERTY('ProductMajorVersion')</c> of the running engine.</summary>
	public async Task<int> EngineMajorVersionAsync(CancellationToken cancellationToken = default)
	{
		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);
		await using SqlCommand command = new("SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int)", connection);
		return await command.ExecuteScalarAsync(cancellationToken) is int majorVersion ?
			majorVersion :
			throw new InvalidOperationException("SERVERPROPERTY('ProductMajorVersion') returned no value.");
	}

	/// <summary>
	///     Creates a database for one test and returns its connection string. Per test, because these tests
	///     build, evolve and tear down the same tables; shared state would let one test's leftovers decide
	///     another's result.
	/// </summary>
	public async Task<string> CreateDatabaseAsync(string name, CancellationToken cancellationToken = default)
	{
		await using SqlConnection connection = new(_connectionString);
		await connection.OpenAsync(cancellationToken);
		await using SqlCommand command = new($"CREATE DATABASE [{name}]", connection);
		await command.ExecuteNonQueryAsync(cancellationToken);
		return new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = name }.ConnectionString;
	}

	// Unconditional: a container that started and then failed the engine floor must not outlive the run, and
	// disposing one that never started is a no-op.
	public ValueTask DisposeAsync() =>
		_container.DisposeAsync();
}
