# Norse.Persistence.EntityFramework

The `INorseDbContext` marker interface, the abstract `NorseDbContext` base, snake_case naming conventions, explicit-length and colocated-entity-configuration enforcement, and the Roslyn generator that discovers `INorseEntity<TSelf>` implementations — provider-agnostic EF foundation shared by all Norse contexts regardless of RDBMS.

Also home to the provider seam itself: `INorseEfProvider`/`INorseEfMigrationProvider` (the contract every provider binding implements — `NorsePostgresEfProvider`, `NorseSqlServerEfProvider`, ...), `NorseNameRewriters` (`LowerSnakeCase`/`UpperSnakeCase`), `AddNorseContext<TContext>()` (pooled runtime registration), and `ApplyNorseProviderOptions()` (the single choreography that wires a provider binding's `Configure`/`NameRewriter`/`EntityRenameHook` into a context — consumed by `AddNorseContext`, `AddNorseMigrationContext`, and the design-time factory alike). Naming is binding data, not a decision this package makes: reference a provider package and pass its `Instance` in.

Part of the [Norse Architecture](https://github.com/NorseArchitecture) platform.
