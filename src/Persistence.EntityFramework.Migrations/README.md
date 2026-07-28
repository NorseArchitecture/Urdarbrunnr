# Norse.Persistence.EntityFramework.Migrations

`EfMigrationContributor<TContext>`, `MigrationConnectionStringAttribute`, and the provider-neutral `AddNorseMigrationContext<TContext>` migration-host choreography. Ships the provider-agnostic Roslyn generator that discovers migration contributors, seed contributors, and the single provider binding visible in a migrations service's compilation, then emits `AddNorseMigrations()`. The generator reports NORSE030–034 for anything ambiguous or missing at compile time — no provider binding, more than one provider binding, no `ModelSnapshot` for a context, more than one `ModelSnapshot` for a context, and a provider binding missing its required `Instance` member. Reference this one package (`Generator="true"`) from your migrations service.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
