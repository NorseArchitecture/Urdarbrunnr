namespace Norse.Persistence.EntityFramework;

/// <summary>
/// The engine-native identifier rewriters a provider binding exposes via
/// <c>INorseEfProvider.NameRewriter</c> (declared in Task 3). The rewrite algorithm itself stays
/// internal in <see cref="SnakeCaseNameRewriter"/>; these are the only public entry points, one per
/// engine-native style — the binding picks one (or none), realms never choose.
/// </summary>
public static class NorseNameRewriters
{
	/// <summary>
	/// Rewrites an identifier to lower snake_case — PostgreSQL's escape-free native style
	/// (unquoted identifiers fold to lowercase there).
	/// </summary>
	/// <param name="name">The identifier to rewrite.</param>
	/// <returns>The snake_case identifier.</returns>
	public static string LowerSnakeCase(string name) =>
		SnakeCaseNameRewriter.RewriteName(name, uppercase: false);

	/// <summary>
	/// Rewrites an identifier to UPPER_SNAKE_CASE — Oracle's escape-free native style
	/// (unquoted identifiers fold to uppercase there).
	/// </summary>
	/// <param name="name">The identifier to rewrite.</param>
	/// <returns>The UPPER_SNAKE_CASE identifier.</returns>
	public static string UpperSnakeCase(string name) =>
		SnakeCaseNameRewriter.RewriteName(name, uppercase: true);
}
