using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norse.Abstractions.Migrations;
using Norse.Abstractions.Migrations.Seeding;
using Norse.Persistence.EntityFramework.SqlServer;

namespace Norse.Persistence.EntityFramework.Design.SqlServer.Generator.Tests;

public sealed class MigrationContributorGeneratorTests
{
	[Fact]
	void Generator_produces_AddNorseMigrations_method()
	{
		const string Source = """
			using Norse.Persistence.EntityFramework;
			using Norse.Persistence.EntityFramework.Design;
			using Microsoft.EntityFrameworkCore;

			[MigrationConnectionString("test-db")]
			sealed class TestContributor(TestContext ctx) : EfMigrationContributor<TestContext>(ctx)
			{
				public override string Name => "Test";
			}

			sealed class TestContext(DbContextOptions<TestContext> opts) : NorseDbContext(opts);
			""";

		var compilation = CreateCompilation(Source);
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
		const string Source = """
		using Microsoft.Extensions.DependencyInjection;
		using Norse.Abstractions.Migrations.Seeding;

		sealed class TestSeedContributor : ISeedContributor
		{
			public string Name => "Test";
			public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
			public static void ConfigureServices(IServiceCollection services) { }
		}
		""";

		var compilation = CreateCompilation(Source);
		MigrationContributorGenerator generator = new();
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
		// original source (plus a minimal stand-in for Midgard's runner extensions, which Urðarbrunnr
		// cannot reference directly — it sits below Midgard in the platform's dependency chain) and
		// asserts there are zero error diagnostics.
		const string Source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Norse.Persistence.EntityFramework;
			using Norse.Persistence.EntityFramework.Design;
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
			// Stand-in for Norse.Infrastructure.Migrations (Midgard) -- Urðarbrunnr sits below Midgard in
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

		var compilation = CreateCompilation(Source);
		MigrationContributorGenerator generator = new();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		result.GeneratedTrees.Length.ShouldBe(1);

		List<MetadataReference> references = [.. ReferenceAssemblies()
			.Append(MetadataReference.CreateFromFile(typeof(IHostApplicationBuilder).Assembly.Location))
			.Append(MetadataReference.CreateFromFile(typeof(NorseSqlServerContextExtensions).Assembly.Location))];

		var recompiled = CSharpCompilation.Create(
			"TestAssembly.Recompiled",
			[
				CSharpSyntaxTree.ParseText(Source, cancellationToken: TestContext.Current.CancellationToken),
				CSharpSyntaxTree.ParseText(InfrastructureStub, cancellationToken: TestContext.Current.CancellationToken),
				result.GeneratedTrees[0],
			],
			references,
			new(OutputKind.DynamicallyLinkedLibrary));

		IList<Diagnostic> errors = [.. recompiled.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error)];

		errors.ShouldBeEmpty();
	}

	[Fact]
	void Generator_produces_AddNorseSeedingRunner_call_even_with_zero_seed_contributors()
	{
		const string Source = """
			using Norse.Persistence.EntityFramework;
			using Norse.Persistence.EntityFramework.Design;
			using Microsoft.EntityFrameworkCore;

			[MigrationConnectionString("test-db")]
			sealed class TestContributor(TestContext ctx) : EfMigrationContributor<TestContext>(ctx)
			{
				public override string Name => "Test";
			}

			sealed class TestContext(DbContextOptions<TestContext> opts) : NorseDbContext(opts);
			""";

		var compilation = CreateCompilation(Source);
		MigrationContributorGenerator generator = new();
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
			new(OutputKind.DynamicallyLinkedLibrary));

	static IList<MetadataReference> ReferenceAssemblies()
	{
		// Build metadata references from explicit assembly locations — AppDomain.GetAssemblies()
		// is unreliable in .NET 11 due to metadata pre-sharing; typeof().Assembly.Location is stable.
		// In .NET 5+ the public Attribute/Object surface lives in System.Runtime.dll (a facade), not
		// System.Private.CoreLib — both must be present for Roslyn to bind attribute constructors.
		var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
		IList<MetadataReference> references = [.. new[]
		{
			typeof(object),
			typeof(MigrationConnectionStringAttribute),
			typeof(NorseDbContext),
			typeof(IMigrationContributor),
			typeof(ISeedContributor),
			typeof(IServiceCollection),
			typeof(DbContext)
		}
		.Select(t => MetadataReference.CreateFromFile(t.Assembly.Location))
		.Cast<MetadataReference>()
		.Append(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")))];

		return references;
	}
}
