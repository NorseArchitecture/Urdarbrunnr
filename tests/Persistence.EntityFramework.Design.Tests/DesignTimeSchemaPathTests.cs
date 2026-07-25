namespace Norse.Persistence.EntityFramework.Design.Tests;

public sealed class DesignTimeSchemaPathTests
{
	const string
		DatabaseName = "norse_reference",
		AssemblyName = "Realm.Migrations.PostgreSQL";

	[Fact]
	void Resolve_walks_up_three_levels_from_build_output_to_project_root()
	{
		var buildOutput = Path.Combine("repo", AssemblyName, "bin", "Debug", "net11.0");

		var result = DesignTimeSchemaPath.Resolve(buildOutput, DatabaseName);

		result.ShouldBe(Path.Combine("repo", AssemblyName, "schema", $"{DatabaseName}.sql"));
	}

	[Fact]
	void Resolve_throws_when_the_base_directory_is_too_shallow_to_have_a_project_root()
	{
		var buildOutput = Path.Combine("bin", "Debug");

		Should.Throw<InvalidOperationException>(() => DesignTimeSchemaPath.Resolve(buildOutput, DatabaseName));
	}

	[Fact]
	void Resolve_is_invariant_to_a_trailing_directory_separator_matching_AppContext_BaseDirectory_shape()
	{
		var buildOutputWithoutTrailingSeparator = Path.Combine("repo", AssemblyName, "bin", "Debug", "net11.0");
		var buildOutputWithTrailingSeparator = $"{buildOutputWithoutTrailingSeparator}{Path.DirectorySeparatorChar}";

		var resultWithoutTrailingSeparator = DesignTimeSchemaPath.Resolve(buildOutputWithoutTrailingSeparator, DatabaseName);
		var resultWithTrailingSeparator = DesignTimeSchemaPath.Resolve(buildOutputWithTrailingSeparator, DatabaseName);

		resultWithTrailingSeparator.ShouldBe(resultWithoutTrailingSeparator);
	}
}
