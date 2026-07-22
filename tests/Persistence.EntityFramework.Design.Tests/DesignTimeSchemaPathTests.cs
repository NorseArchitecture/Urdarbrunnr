namespace Norse.Persistence.EntityFramework.Design.Tests;

public sealed class DesignTimeSchemaPathTests
{
	[Fact]
	void Resolve_walks_up_three_levels_from_build_output_to_project_root()
	{
		var buildOutput = Path.Combine("repo", "Realm.Migrations.PostgreSQL", "bin", "Debug", "net10.0");

		var result = DesignTimeSchemaPath.Resolve(buildOutput, "norse_referencedata");

		result.ShouldBe(Path.Combine("repo", "Realm.Migrations.PostgreSQL", "schema", "norse_referencedata.sql"));
	}

	[Fact]
	void Resolve_throws_when_the_base_directory_is_too_shallow_to_have_a_project_root()
	{
		var buildOutput = Path.Combine("bin", "Debug");

		Should.Throw<InvalidOperationException>(() => DesignTimeSchemaPath.Resolve(buildOutput, "norse_referencedata"));
	}
}
