# Norse.Persistence.EntityFramework.Design

`DdlEmittingMigrationsScaffolder`, `AddNorseDesignTimeServices()`, and the provider-neutral `NorseDesignTimeDbContextFactory<TContext>` base — referenced only by realm `*.Migrations.{Provider}` projects; never by runtime containers; never connects to a database. A downstream realm's `IDesignTimeServices` calls `AddNorseDesignTimeServices()` to install the scaffolder as EF's `IMigrationsScaffolder`, so every `dotnet ef migrations add`/`remove` writes the current-state schema as plain DDL to a checked-in `schema/{databaseName}.sql` file. `NorseDesignTimeDbContextFactory<TContext>` is abstract — a downstream factory implements `ProviderBinding` to name its `INorseEfProvider` and builds against that provider's inert placeholder connection string. Deliberately no environment-variable escape hatch.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
