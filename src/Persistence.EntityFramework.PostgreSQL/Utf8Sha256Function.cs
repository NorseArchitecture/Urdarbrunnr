using System.Text.RegularExpressions;

namespace Norse.Persistence.EntityFramework.PostgreSQL;

/// <summary>
///     The chassis-owned <c>norse_utf8_sha256(text)</c> (identity split spec §4.6, r9, ruled 2026-10-24): SHA-256
///     over a value's UTF-8 bytes, usable in a generation expression. The inline form
///     <c>sha256(convert_to(…, 'UTF8'))</c> is refused there (<c>42P17</c>), because <c>convert_to</c> is
///     <c>STABLE</c>; the wrapper's <c>IMMUTABLE</c> marking is honest, since the same characters give the same
///     UTF-8 bytes whatever the server encoding. The migrations generator emits it idempotently, ahead of any
///     table whose computed column SQL names it, and only then — so regenerating a migration can never lose it
///     and a model that never names it never gets it.
/// </summary>
static partial class Utf8Sha256Function
{
	/// <summary>The function, verbatim from spec §4.6. <c>CREATE OR REPLACE</c>: every migration that names it re-emits it.</summary>
	public const string Sql =
		"CREATE OR REPLACE FUNCTION norse_utf8_sha256(text) RETURNS bytea LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE RETURN pg_catalog.sha256(pg_catalog.convert_to($1, 'UTF8'));";

	/// <summary>Whether a computed column's SQL calls the function.</summary>
	/// <param name="computedColumnSql">The column's generation expression, if it has one.</param>
	public static bool IsNamedBy(string? computedColumnSql) =>
		computedColumnSql is not null && Reference().IsMatch(computedColumnSql);

	[GeneratedRegex(@"\bnorse_utf8_sha256""?\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex Reference();
}
