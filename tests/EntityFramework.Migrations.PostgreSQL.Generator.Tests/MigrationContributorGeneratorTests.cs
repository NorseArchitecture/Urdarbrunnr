using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.EntityFramework.Migrations.PostgreSQL.Generator.Tests;

public sealed class MigrationContributorGeneratorTests
{
	[Fact]
	void Generator_produces_AddNorseMigrations_method()
	{
		var source = """
			using Norse.EntityFramework;
			using Norse.EntityFramework.Migrations;
			using Microsoft.EntityFrameworkCore;

			[MigrationConnectionString("test-db")]
			sealed class TestContributor(TestContext ctx) : EfMigrationContributor<TestContext>(ctx)
			{
				public override string Name => "Test";
			}

			sealed class TestContext(DbContextOptions<TestContext> opts) : NorseDbContext(opts);
			""";

		var compilation = CreateCompilation(source);
		var generator = new MigrationContributorGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		result.GeneratedTrees.Length.ShouldBe(1);
		var generated = result.GeneratedTrees[0].ToString();
		generated.ShouldContain("AddNorseMigrations");
		generated.ShouldContain("AddNorseMigrationsRunner");
		generated.ShouldContain("TestContributor");
		generated.ShouldContain("test-db");
		generated.ShouldContain("AddNorsePostgresMigrationContext");
		generated.ShouldContain("\"TestAssembly\"");
	}

	[Fact]
	void Generator_emits_no_source_when_no_contributors_found()
	{
		var compilation = CreateCompilation("// empty");
		var generator = new MigrationContributorGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		result.GeneratedTrees.ShouldBeEmpty();
	}

	[Fact]
	void Generator_discovers_seed_contributors_and_emits_registration()
	{
		var source = """
			using Microsoft.Extensions.DependencyInjection;
			using Norse.Abstractions.Migrations.Seeding;

			sealed class TestSeedContributor : ISeedContributor
			{
				public string Name => "Test";
				public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
				public static void ConfigureServices(IServiceCollection services) { }
			}
			""";

		var compilation = CreateCompilation(source);
		var generator = new MigrationContributorGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		result.GeneratedTrees.Length.ShouldBe(1);
		var generated = result.GeneratedTrees[0].ToString();
		generated.ShouldContain("TestSeedContributor.ConfigureServices(builder.Services);");
		generated.ShouldContain("AddTransient<global::Norse.Abstractions.Migrations.Seeding.ISeedContributor, global::TestSeedContributor>");
		generated.ShouldContain("AddNorseSeedingRunner");
	}

	[Fact]
	void Generator_produces_AddNorseSeedingRunner_call_even_with_zero_seed_contributors()
	{
		var source = """
			using Norse.EntityFramework;
			using Norse.EntityFramework.Migrations;
			using Microsoft.EntityFrameworkCore;

			[MigrationConnectionString("test-db")]
			sealed class TestContributor(TestContext ctx) : EfMigrationContributor<TestContext>(ctx)
			{
				public override string Name => "Test";
			}

			sealed class TestContext(DbContextOptions<TestContext> opts) : NorseDbContext(opts);
			""";

		var compilation = CreateCompilation(source);
		var generator = new MigrationContributorGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		var generated = result.GeneratedTrees[0].ToString();
		generated.ShouldContain("AddNorseSeedingRunner");
	}

	static Compilation CreateCompilation(string source)
	{
		// Build metadata references from explicit assembly locations — AppDomain.GetAssemblies()
		// is unreliable in .NET 11 due to metadata pre-sharing; typeof().Assembly.Location is stable.
		// In .NET 5+ the public Attribute/Object surface lives in System.Runtime.dll (a facade), not
		// System.Private.CoreLib — both must be present for Roslyn to bind attribute constructors.
		var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
		var references = new[]
		{
			typeof(object),
			typeof(Norse.EntityFramework.Migrations.MigrationConnectionStringAttribute),
			typeof(Norse.EntityFramework.NorseDbContext),
			typeof(Norse.Abstractions.Migrations.IMigrationContributor),
			typeof(Norse.Abstractions.Migrations.Seeding.ISeedContributor),
			typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection),
			typeof(Microsoft.EntityFrameworkCore.DbContext),
		}
		.Select(t => MetadataReference.CreateFromFile(t.Assembly.Location))
		.Cast<MetadataReference>()
		.Append(MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")))
		.ToList();

		return CSharpCompilation.Create(
			"TestAssembly",
			[CSharpSyntaxTree.ParseText(source)],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
	}
}
