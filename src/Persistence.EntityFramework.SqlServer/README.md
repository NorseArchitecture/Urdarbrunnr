# Norse.Persistence.EntityFramework.SqlServer

The SQL Server provider binding — `NorseSqlServerEfProvider`, one sealed class exposed as `public static Instance`, implementing `INorseEfMigrationProvider`. `UseSqlServer` wiring with a forced compatibility-level-170 floor, Aspire enrichment, raw PascalCase naming (`NameRewriter` is `null` — SQL Server's engine-native default needs no rewriter), and the temporal history-table rename hook (paired with the null rewriter, inert by construction). Reference this package and pass `NorseSqlServerEfProvider.Instance` to `AddNorseContext<TContext>` (runtime hosts) or `AddNorseMigrationContext<TContext>` (migrations services) from `Norse.Persistence.EntityFramework`/`.Migrations`.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
