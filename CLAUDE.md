# CLAUDE.md — Urdarbrunnr (`Norse.EntityFramework`)

## 0. Wrong Root — Halt

If you are reading this because **Urdarbrunnr itself is the Claude Code session root** — someone ran `claude` from inside this directory instead of `../Bifrost` — stop here. Do not read further, do not propose changes, do not run anything.

Tell the user: every Norse Architecture session starts from **Bifrost**. Org-wide settings (the `superpowers` plugin, permission rules) only apply when Bifrost is the actual session root — Claude Code never merges a submodule's own `.claude/settings.json` into a parent-launched session. Exit, `cd ../Bifrost`, and run `claude` there instead.

This repo's own `.claude/settings.json` carries a `SessionStart` hook that should already have blocked this session before this file was ever read. If you're reading this anyway, hooks were bypassed, disabled, or failed — halt regardless; this rule does not depend on the hook to hold.

---

> **Do not commit, push, or rewrite git history.** Stage edits (`git add`), show the diff, and stop — the human reviews and commits.

> **Use US English spelling** in code, identifiers, comments, docs, and commit/PR copy.

## 1. What This Repository Is

Urdarbrunnr is **the well's record** — `Norse.EntityFramework`: entity base types, `DbContext` foundations, conventions, value converters, and the migrations chassis. It is the EF Core foundation that Midgard's concrete repository implementations ride on, governed by the contracts Asgard declares. In the dependency chain it sits below Asgard and Svartalfheim, and above Midgard.

**Four assemblies are live**, shipped, tagged, and published to NuGet as Tasks 3–6 of the cross-realm
migrations framework rollout (`../Glitnir/docs/Platform/plans/2026-06-28-migrations-framework-identity-schema.md`).
A SQL Server-parallel trio (`Norse.EntityFramework.SqlServer`, `.Migrations.SqlServer`,
`.Migrations.SqlServer.Generator`) landed alongside them per
`../Glitnir/docs/Urdarbrunnr/specs/2026-07-03-provider-aware-length-and-naming-conventions.md` — `[FixedLength(n)]`
now only translates to `.IsFixedLength()` on SQL Server (Postgres's own docs say `character(n)` has no
storage/performance benefit over `character varying(n)` there), and snake_case naming is an explicit,
overridable opt-in/opt-out on every provider's registration extension rather than baked into
`NorseDbContext`:

- `Norse.EntityFramework` — the `INorseDbContext` marker interface, the abstract `NorseDbContext` base, and the snake_case naming conventions every Norse context inherits.
- `Norse.EntityFramework.PostgreSQL` — `AddNorsePostgresContext<TContext>()`, the canonical Aspire-wired Postgres registration for runtime contexts.
- `Norse.EntityFramework.Migrations` — `EfMigrationContributor<TContext>` and `MigrationConnectionStringAttribute`, the EF-specific base that realm `.Migrations` projects implement.
- `Norse.EntityFramework.Migrations.PostgreSQL` (+ its `.Generator` sibling) — ships this realm's first Roslyn `IIncrementalGenerator`, which discovers every `EfMigrationContributor<TContext>` visible in a migrations service's compilation and emits `AddNorseMigrations()`. It walks **compiled assembly symbols**, never source syntax trees, by design — the plan's verification gate proved identical output whether contributor packages arrive as `ProjectReference` (Bifrost dev mode, today) or `PackageReference` (NuGet/CI mode, tomorrow) before calling the task done.

This is the realm's proving ground for compile-time enforcement over runtime guessing: the generator is live proof the pattern survives contact with both reference modes, not just the happy path. Before writing any code beyond what's shipped: brainstorm → spec → plan, recorded in `../Glitnir/docs/Urdarbrunnr/`, per the org's spec-first discipline. Do not scaffold a project structure ahead of a converged spec. When the next plan is written, its REQUIRED SUB-SKILL line names `superpowers:subagent-driven-development` as the default (not a recommendation among equals — `executing-plans` is the narrow fallback for separate-session review checkpoints) paired with `superpowers:test-driven-development` — implementation here is subagent-orchestrated and test-driven, never one without the other (`../Glitnir/CLAUDE.md` §2.8).

See `../Bifrost/CLAUDE.md` (§2 The Naming Model) and `../Glitnir/CLAUDE.md` (§1 Bounded Context Map) for the full realm table and how Urdarbrunnr fits the rest of the cosmos.
