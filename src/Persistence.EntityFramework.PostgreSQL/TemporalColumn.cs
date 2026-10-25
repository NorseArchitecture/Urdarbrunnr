namespace Norse.Persistence.EntityFramework.PostgreSQL;

/// <summary>
///     One main-table column as the temporal apparatus sees it: everything the history projection, the
///     versioning function, and the timeline view need, and nothing they must never copy (defaults, identity,
///     the generation expression itself).
/// </summary>
/// <param name="Name">The column name.</param>
/// <param name="StoreType">The store type — the only property the history column takes besides the name (§3.4).</param>
/// <param name="IsStoredGenerated">
///     Whether the main column is a stored generated column (<c>GENERATED ALWAYS AS (…) STORED</c>). It still
///     projects into history as a plain column and is copied there from <c>OLD</c>, where it is materialized,
///     but it is left out of the no-op comparison: generated values are computed after <c>BEFORE</c> triggers,
///     so <c>NEW.&lt;generated&gt;</c> is <c>NULL</c> there and comparing it would mark every update a change
///     (chassis spec §3.2 and §3.4, amended 2026-10-24 by identity PoC G6).
/// </param>
readonly record struct TemporalColumn(string Name, string StoreType, bool IsStoredGenerated);
