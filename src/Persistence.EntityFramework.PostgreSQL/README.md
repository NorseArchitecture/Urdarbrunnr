# Norse.Persistence.EntityFramework.PostgreSQL

The PostgreSQL provider binding — `NorsePostgresEfProvider`, one sealed class exposed as `public static Instance`, implementing `INorseEfMigrationProvider`. `UseNpgsql` wiring, Aspire enrichment, engine-native lower snake_case naming (`NameRewriter`), and the inert design-time placeholder connection string. Reference this package and pass `NorsePostgresEfProvider.Instance` to `AddNorseContext<TContext>` (runtime hosts) or `AddNorseMigrationContext<TContext>` (migrations services) from `Norse.Persistence.EntityFramework`/`.Migrations`.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
