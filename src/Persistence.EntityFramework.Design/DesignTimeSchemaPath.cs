namespace Norse.Persistence.EntityFramework.Design;

/// <summary>
/// Resolves the checked-in schema file's path from a design-time-tooling build output directory
/// (<c>AppContext.BaseDirectory</c>, standard <c>bin/{Configuration}/{TargetFramework}/</c> layout).
/// A pure string operation -- built on <see cref="Path.GetDirectoryName(string)"/>, never
/// <see cref="Directory.GetParent(string)"/>, which internally fully-qualifies its input via
/// <see cref="Path.GetFullPath(string)"/> and so silently resolves a relative path against the
/// current working directory instead of staying relative. This is safe to call before the target
/// directory exists and safe to unit test without one.
/// </summary>
static class DesignTimeSchemaPath
{
	internal static string Resolve(string outputBaseDirectory, string databaseName)
	{
		var projectRoot = Up(Up(Up(outputBaseDirectory)))
			?? throw new InvalidOperationException(
				$"Could not resolve a project root three directory levels above '{outputBaseDirectory}'. " +
				"Expected a standard bin/{Configuration}/{TargetFramework} build output layout.");

		return Path.Combine(projectRoot, "schema", $"{databaseName}.sql");
	}

	static string? Up(string? path) =>
		string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);
}
