# Norse.Persistence.EntityFramework.Design.SqlServer

Pulls in `Norse.Persistence.EntityFramework.Design` (contributor base) and `Norse.Persistence.EntityFramework.SqlServer` (Aspire SQL Server wiring) and ships the Roslyn generator that discovers every `EfMigrationContributor<TContext>` and `ISeedContributor` and emits a single `AddNorseMigrations()` wiring both, plus `NorseSqlServerDesignTimeDbContextFactory<TContext>`, the `IDesignTimeDbContextFactory<TContext>` base a downstream realm's own design-time factory derives from. Reference this single package from your migrations service.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
