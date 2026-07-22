# CLAUDE.md — Urðarbrunnr (`Norse.Persistence`)

## 0. Wrong Root — Halt

Session root must be **Bifröst**, not this repo directly — org-wide settings (`superpowers`, permission rules) only apply from the actual root, and Claude Code never merges a submodule's own `.claude/settings.json` into a parent-launched session. If `claude` was run from inside **Urðarbrunnr**, stop: don't read further, don't propose changes, don't run anything — tell the user to `cd ../Bifrost` and start there. (This repo's `.claude/settings.json` carries a `SessionStart` hook meant to block this before you ever see this file; if you're reading this anyway, the hook was bypassed, disabled, or failed — halt regardless.)

> **Do not commit, push, or rewrite git history** — stage (`git add`), show the diff, stop; the human reviews and commits. This applies even when a skill's flow includes a commit step. **US English spelling** everywhere — code, comments, docs, commits.

## 1. What This Repository Is

Urðarbrunnr is **the well's record** — `Norse.Persistence`: the platform's persistence realm, scoped to all database and data-store activity, not just EF Core. `Norse.Persistence.EntityFramework` is today's live vendor family — entity base types, `DbContext` foundations, conventions, value converters, and the migrations chassis, the EF Core foundation that Midgard's concrete repository implementations ride on, governed by the contracts Asgard declares — but it is one branch, not the ceiling. A different ORM (Dapper, NHibernate, SqlSugar), a native driver that bypasses an ORM entirely, or a document/search store (MongoDB, Elasticsearch) lands here the same way Heimdall took on FluentUI: drop in the vendor-specific `.csproj` and package references as a sibling under `Norse.Persistence.*` (`Norse.Persistence.Dapper`, `Norse.Persistence.MongoDB`, …), wire it up, done — no separate realm, no new repo. In the dependency chain it sits below Asgard and Svartálfheim, and above Midgard.

**Four assemblies are live**, shipped, tagged, and published to NuGet as Tasks 3–6 of the cross-realm
migrations framework rollout (`../Glitnir/docs/Platform/plans/2026-06-28-migrations-framework-identity-schema.md`).
A SQL Server-parallel trio (`Norse.Persistence.EntityFramework.SqlServer`, `.Migrations.SqlServer`,
`.Migrations.SqlServer.Generator`) landed alongside them per
`../Glitnir/docs/Urdarbrunnr/specs/2026-07-03-provider-aware-length-and-naming-conventions.md` — `[FixedLength(n)]`
now only translates to `.IsFixedLength()` on SQL Server (Postgres's own docs say `character(n)` has no
storage/performance benefit over `character varying(n)` there), and snake_case naming is an explicit,
overridable opt-in/opt-out on every provider's registration extension rather than baked into
`NorseDbContext`:

- `Norse.Persistence.EntityFramework` — the `INorseDbContext` marker interface, the abstract `NorseDbContext` base, and the snake_case naming conventions every Norse context inherits.
- `Norse.Persistence.EntityFramework.PostgreSQL` — `AddNorsePostgresContext<TContext>()`, the canonical Aspire-wired Postgres registration for runtime contexts.
- `Norse.Persistence.EntityFramework.Migrations` — `EfMigrationContributor<TContext>` and `MigrationConnectionStringAttribute`, the EF-specific base that realm `.Migrations` projects implement.
- `Norse.Persistence.EntityFramework.Migrations.PostgreSQL` (+ its `.Generator` sibling) — ships this realm's first Roslyn `IIncrementalGenerator`, which discovers every `EfMigrationContributor<TContext>` visible in a migrations service's compilation and emits `AddNorseMigrations()`. It walks **compiled assembly symbols**, never source syntax trees, by design — the plan's verification gate proved identical output whether contributor packages arrive as `ProjectReference` (Bifröst dev mode, today) or `PackageReference` (NuGet/CI mode, tomorrow) before calling the task done.

Snake_case naming, previously provided by the external `EFCore.NamingConventions` package, is now implemented in-house via `NorseSnakeCaseNamingConvention` and `SnakeCaseNameRewriter` in `Norse.Persistence.EntityFramework`; the dependency has been removed. The registration API — `useSnakeCaseNaming` parameter defaults and override mechanics on `AddNorsePostgresContext` and `AddNorseSqlServerContext` — remains unchanged. Full design: `../Glitnir/docs/Urdarbrunnr/specs/2026-07-22-inhouse-snake-case-naming-convention-design.md`.

This is the realm's proving ground for compile-time enforcement over runtime guessing: the generator is live proof the pattern survives contact with both reference modes, not just the happy path. Before writing any code beyond what's shipped: brainstorm → spec → plan, recorded in `../Glitnir/docs/Urdarbrunnr/`, per the org's spec-first discipline. Do not scaffold a project structure ahead of a converged spec. When the next plan is written, its REQUIRED SUB-SKILL line names `superpowers:subagent-driven-development` as the default (not a recommendation among equals — `executing-plans` is the narrow fallback for separate-session review checkpoints) paired with `superpowers:test-driven-development` — implementation here is subagent-orchestrated and test-driven, never one without the other (`../Glitnir/CLAUDE.md` §2.8).

See `../Bifrost/CLAUDE.md` (§2 The Naming Model) and `../Glitnir/CLAUDE.md` (§3 Bounded Context Map) for the full realm table and how Urðarbrunnr fits the rest of the cosmos.
