using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework.Generator.Tests;

public sealed class EntityConfigurationApplicationGeneratorTests
{
	[Fact]
	void Generator_emits_ApplyNorseConfigurations_for_Tier1_and_Tier2_entities()
	{
		const string Source = """
		                      using Microsoft.EntityFrameworkCore.Metadata.Builders;
		                      using Norse.Persistence.EntityFramework;

		                      sealed record Tier1Entity : NorseEntityBase<Tier1Entity>, INorseEntity<Tier1Entity>
		                      {
		                      	public static void Configure(EntityTypeBuilder<Tier1Entity> builder) { }
		                      }

		                      sealed record Tier2Entity : INorseEntity<Tier2Entity>
		                      {
		                      	public static void Configure(EntityTypeBuilder<Tier2Entity> builder) { }
		                      }
		                      """;

		var compilation = CreateCompilation(Source);
		EntityConfigurationApplicationGenerator generator = new();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _,
			TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		var generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
		generated.ShouldContain("ApplyNorseConfigurations");
		generated.ShouldContain("Tier1Entity.Configure");
		generated.ShouldContain("Tier2Entity.Configure");
	}

	[Fact]
	void Generator_emits_Tier1_partial_override_for_partial_NorseDbContext_subclass()
	{
		const string Source = """
		                      using Microsoft.EntityFrameworkCore;
		                      using Microsoft.EntityFrameworkCore.Metadata.Builders;
		                      using Norse.Persistence.EntityFramework;

		                      sealed record Tier1Entity : NorseEntityBase<Tier1Entity>, INorseEntity<Tier1Entity>
		                      {
		                      	public static void Configure(EntityTypeBuilder<Tier1Entity> builder) { }
		                      }

		                      partial class MyContext(DbContextOptions<MyContext> options) : NorseDbContext(options);
		                      """;

		var compilation = CreateCompilation(Source);
		EntityConfigurationApplicationGenerator generator = new();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _,
			TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		var generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
		generated.ShouldContain("partial class MyContext");
		generated.ShouldContain("ConfigureNorseEntities");
	}

	[Fact]
	void
		Generator_suppresses_CS1591_around_the_partial_override_so_consumers_never_carry_the_NoWarn_themselves()
	{
		// ConfigureNorseEntities is protected on a public partial NorseDbContext subclass — publicly
		// visible with no XML doc comment. CS1591 must be suppressed here, in the generator, not
		// worked around via <NoWarn> in every consuming .csproj.
		const string Source = """
		                      using Microsoft.EntityFrameworkCore;
		                      using Microsoft.EntityFrameworkCore.Metadata.Builders;
		                      using Norse.Persistence.EntityFramework;

		                      sealed record Tier1Entity : NorseEntityBase<Tier1Entity>, INorseEntity<Tier1Entity>
		                      {
		                      	public static void Configure(EntityTypeBuilder<Tier1Entity> builder) { }
		                      }

		                      partial class MyContext(DbContextOptions<MyContext> options) : NorseDbContext(options);
		                      """;

		var compilation = CreateCompilation(Source);
		EntityConfigurationApplicationGenerator generator = new();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _,
			TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		var generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
		generated.ShouldContain("#pragma warning disable CS1591");
		generated.ShouldContain("#pragma warning restore CS1591");
	}

	[Fact]
	void Generator_emits_namespaced_Tier1_partial_override_as_valid_C_sharp()
	{
		const string Source = """
		                      using Microsoft.EntityFrameworkCore;
		                      using Microsoft.EntityFrameworkCore.Metadata.Builders;
		                      using Norse.Persistence.EntityFramework;

		                      namespace Foo.Bar
		                      {
		                      	sealed record Tier1Entity : NorseEntityBase<Tier1Entity>, INorseEntity<Tier1Entity>
		                      	{
		                      		public static void Configure(EntityTypeBuilder<Tier1Entity> builder) { }
		                      	}

		                      	partial class MyContext(DbContextOptions<MyContext> options) : NorseDbContext(options);
		                      }
		                      """;

		var compilation = CreateCompilation(Source);
		EntityConfigurationApplicationGenerator generator = new();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics,
			TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		var generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
		generated.ShouldContain("namespace Foo.Bar");
		generated.ShouldContain("partial class MyContext");
		generated.ShouldContain("ConfigureNorseEntities");

		diagnostics.ShouldBeEmpty();

		foreach (var parsed in result.GeneratedTrees.Select(tree =>
			CSharpSyntaxTree.ParseText(tree.ToString(),
				cancellationToken: TestContext.Current.CancellationToken)))
			parsed.GetDiagnostics(TestContext.Current.CancellationToken).ShouldBeEmpty();

		outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken).ShouldBeEmpty();
	}

	[Fact]
	void Generator_emits_no_source_when_no_entities_found()
	{
		var compilation = CreateCompilation("// empty");
		EntityConfigurationApplicationGenerator generator = new();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _,
			TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		result.GeneratedTrees.ShouldBeEmpty();
	}

	[Fact]
	void Generator_skips_abstract_and_generic_candidates()
	{
		const string Source = """
		                      using Microsoft.EntityFrameworkCore.Metadata.Builders;
		                      using Norse.Persistence.EntityFramework;

		                      abstract class AbstractEntity : NorseEntityBase<AbstractEntity>, INorseEntity<AbstractEntity>
		                      {
		                      	public static void Configure(EntityTypeBuilder<AbstractEntity> builder) { }
		                      }
		                      """;

		var compilation = CreateCompilation(Source);
		EntityConfigurationApplicationGenerator generator = new();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _,
			TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		result.GeneratedTrees.ShouldBeEmpty();
	}

	static Compilation CreateCompilation(string source)
	{
		var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
		IList<MetadataReference> references =
		[
			.. new[] { typeof(object), typeof(INorseEntity<>), typeof(DbContext), typeof(EntityTypeBuilder<>) }
				.Select(t => MetadataReference.CreateFromFile(t.Assembly.Location))
				.Cast<MetadataReference>()
				.Append(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")))
		];

		return CSharpCompilation.Create(
			"TestAssembly",
			[CSharpSyntaxTree.ParseText(source)],
			references,
			new(OutputKind.DynamicallyLinkedLibrary));
	}
}
