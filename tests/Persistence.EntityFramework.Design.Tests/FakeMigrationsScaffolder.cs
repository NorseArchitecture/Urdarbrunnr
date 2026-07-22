using Microsoft.EntityFrameworkCore.Migrations.Design;

namespace Norse.Persistence.EntityFramework.Design.Tests;

/// <summary>
/// Records calls instead of doing real scaffolding -- shared by <see cref="DdlEmittingMigrationsScaffolderTests"/>
/// and <c>NorseDesignTimeServicesExtensionsTests</c> (added in a later step of this same task), which
/// both need to verify a call reached EF's (fake) original registration.
/// </summary>
sealed class FakeMigrationsScaffolder : IMigrationsScaffolder
{
	public int ScaffoldMigrationCallCount { get; private set; }
	public int RemoveMigrationCallCount { get; private set; }
	public int SaveCallCount { get; private set; }

	public ScaffoldedMigration ScaffoldMigration(string migrationName, string? rootNamespace, string? subNamespace = null, string? language = null, bool dryRun = false)
	{
		ScaffoldMigrationCallCount++;
		return new ScaffoldedMigration("cs", null, "", "20260722000000_Test", "", "", "", "", "");
	}

	public MigrationFiles RemoveMigration(string projectDir, string? rootNamespace, bool force, string? language, bool dryRun = false, bool offline = false)
	{
		RemoveMigrationCallCount++;
		return new MigrationFiles();
	}

	public MigrationFiles Save(string projectDir, ScaffoldedMigration migration, string? outputDir, bool dryRun = false)
	{
		SaveCallCount++;
		return new MigrationFiles();
	}
}
