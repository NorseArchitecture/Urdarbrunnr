using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.Persistence.EntityFramework.Migrations.SqlServer.Generator.Tests;

public sealed class MigrationContributorGeneratorTests
{
	[Fact]
	void Generator_produces_AddNorseMigrations_method()
	{
		var source = """
			using Norse.Persistence.EntityFramework;
			using Norse.Persistence.EntityFramework.Migrations;
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
		generated.ShouldContain("AddNorseSqlServerMigrationContext");
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
		generated.ShouldContain("ConfigureSeedContributor<global::TestSeedContributor>(builder.Services);");
		generated.ShouldContain("AddTransient<global::Norse.Abstractions.Migrations.Seeding.ISeedContributor, global::TestSeedContributor>");
		generated.ShouldContain("AddNorseSeedingRunner");
	}

	[Fact]
	void Generator_emitted_source_compiles_for_seed_contributor_that_does_not_override_ConfigureServices()
	{
		// ISeedContributor.ConfigureServices is `static virtual void ConfigureServices(...) { }` — a
		// no-op default. A concrete type that relies on that default has no `ConfigureServices` member
		// reachable via its own type name, so a direct static call (`SomeContributor.ConfigureServices(...)`)
		// fails with CS0117; static virtual interface members are only invocable via the interface name
		// or via a generic type parameter constrained to the interface. Every other test in this file
		// only proves generation *ran* (`RunGeneratorsAndUpdateCompilation` + string assertions on the
		// generated text) — none of them prove the emitted text actually compiles. This test does: it
		// feeds the real generated tree back through a second, real `CSharpCompilation` alongside the
		// original source (plus a minimal stand-in for Midgard's runner extensions, which Urdarbrunnr
		// cannot reference directly — it sits below Midgard in the platform's dependency chain) and
		// asserts there are zero error diagnostics.
		var source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Norse.Persistence.EntityFramework;
			using Norse.Persistence.EntityFramework.Migrations;
			using Microsoft.EntityFrameworkCore;
			using Microsoft.Extensions.DependencyInjection;
			using Norse.Abstractions.Migrations.Seeding;

			[MigrationConnectionString("test-db")]
			sealed class TestContributor(TestContext ctx) : EfMigrationContributor<TestContext>(ctx)
			{
				public override string Name => "Test";
			}

			sealed class TestContext(DbContextOptions<TestContext> opts) : NorseDbContext(opts);

			sealed class SeedContributorWithoutOverride : ISeedContributor
			{
				public string Name => "WithoutOverride";
				public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			}

			sealed class SeedContributorWithOverride : ISeedContributor
			{
				public string Name => "WithOverride";
				public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
				public static void ConfigureServices(IServiceCollection services) { }
			}
			""";

		const string InfrastructureStub = """
			// Stand-in for Norse.Infrastructure.Migrations (Midgard) -- Urdarbrunnr sits below Midgard in
			// the dependency chain and cannot reference it, so this reproduces just enough of its shape
			// (same namespace, same method names) for the generated code to resolve against.
			namespace Norse.Infrastructure.Migrations
			{
				static class HostApplicationBuilderExtensionsTestStub
				{
					public static Microsoft.Extensions.Hosting.IHostApplicationBuilder AddNorseMigrationsRunner(
						this Microsoft.Extensions.Hosting.IHostApplicationBuilder builder) => builder;

					public static Microsoft.Extensions.Hosting.IHostApplicationBuilder AddNorseSeedingRunner(
						this Microsoft.Extensions.Hosting.IHostApplicationBuilder builder) => builder;
				}
			}
			""";

		var compilation = CreateCompilation(source);
		var generator = new MigrationContributorGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		result.GeneratedTrees.Length.ShouldBe(1);

		var references = ReferenceAssemblies()
			.Append(MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.Hosting.IHostApplicationBuilder).Assembly.Location))
			.Append(MetadataReference.CreateFromFile(typeof(Norse.Persistence.EntityFramework.SqlServer.NorseSqlServerContextExtensions).Assembly.Location))
			.ToList();

		var recompiled = CSharpCompilation.Create(
			"TestAssembly.Recompiled",
			[
				CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken),
				CSharpSyntaxTree.ParseText(InfrastructureStub, cancellationToken: TestContext.Current.CancellationToken),
				result.GeneratedTrees[0],
			],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var errors = recompiled.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.ToList();

		errors.ShouldBeEmpty();
	}

	[Fact]
	void Generator_produces_AddNorseSeedingRunner_call_even_with_zero_seed_contributors()
	{
		var source = """
			using Norse.Persistence.EntityFramework;
			using Norse.Persistence.EntityFramework.Migrations;
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

	static Compilation CreateCompilation(string source) =>
		CSharpCompilation.Create(
			"TestAssembly",
			[CSharpSyntaxTree.ParseText(source)],
			ReferenceAssemblies(),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

	static List<MetadataReference> ReferenceAssemblies()
	{
		// Build metadata references from explicit assembly locations — AppDomain.GetAssemblies()
		// is unreliable in .NET 11 due to metadata pre-sharing; typeof().Assembly.Location is stable.
		// In .NET 5+ the public Attribute/Object surface lives in System.Runtime.dll (a facade), not
		// System.Private.CoreLib — both must be present for Roslyn to bind attribute constructors.
		var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
		var references = new[]
		{
			typeof(object),
			typeof(Norse.Persistence.EntityFramework.Migrations.MigrationConnectionStringAttribute),
			typeof(Norse.Persistence.EntityFramework.NorseDbContext),
			typeof(Norse.Abstractions.Migrations.IMigrationContributor),
			typeof(Norse.Abstractions.Migrations.Seeding.ISeedContributor),
			typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection),
			typeof(Microsoft.EntityFrameworkCore.DbContext),
		}
		.Select(t => MetadataReference.CreateFromFile(t.Assembly.Location))
		.Cast<MetadataReference>()
		.Append(MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")))
		.ToList();

		return references;
	}
}
