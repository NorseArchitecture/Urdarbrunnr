namespace Norse.Persistence.EntityFramework;

/// <summary>
/// The migration-host half of the provider seam. Production-tier providers (Postgres, SQL Server,
/// eventually Oracle) implement this; a local-dev-only provider (SQLite) implements only
/// <see cref="INorseEfProvider"/>, making a SQLite migrations host a compile error rather than a
/// runtime refusal — the tier split is enforced by the type system, not a flag.
/// </summary>
public interface INorseEfMigrationProvider : INorseEfProvider;
