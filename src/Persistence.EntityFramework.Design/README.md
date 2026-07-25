# Norse.Persistence.EntityFramework.Design

`EfMigrationContributor<TContext>` base class and the `MigrationConnectionStringAttribute` — referenced only by migrations service and realm `*.Migrations` projects; never by runtime containers. Also ships `DdlEmittingMigrationsScaffolder` and `AddNorseDesignTimeServices()`: a downstream realm's `IDesignTimeServices` calls the latter to install the former as EF's `IMigrationsScaffolder`, so every `dotnet ef migrations add`/`remove` writes the current-state schema as plain DDL to a checked-in `schema/{databaseName}.sql` file.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
