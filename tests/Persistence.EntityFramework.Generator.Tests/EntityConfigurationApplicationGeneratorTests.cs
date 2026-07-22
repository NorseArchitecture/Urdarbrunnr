using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Norse.Persistence.EntityFramework.Generator.Tests;

public sealed class EntityConfigurationApplicationGeneratorTests
{
	[Fact]
	void Generator_emits_ApplyNorseConfigurations_for_Tier1_and_Tier2_entities()
	{
		var source = """
			using Microsoft.EntityFrameworkCore.Metadata.Builders;
			using Norse.Persistence.EntityFramework;

			sealed class Tier1Entity : NorseEntityBase<Tier1Entity>, INorseEntity<Tier1Entity>
			{
				public static void Configure(EntityTypeBuilder<Tier1Entity> builder) { }
			}

			sealed class Tier2Entity : INorseEntity<Tier2Entity>
			{
				public static void Configure(EntityTypeBuilder<Tier2Entity> builder) { }
			}
			""";

		var compilation = CreateCompilation(source);
		var generator = new EntityConfigurationApplicationGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		var generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
		generated.ShouldContain("ApplyNorseConfigurations");
		generated.ShouldContain("Tier1Entity.Configure");
		generated.ShouldContain("Tier2Entity.Configure");
	}

	[Fact]
	void Generator_emits_Tier1_partial_override_for_partial_NorseDbContext_subclass()
	{
		var source = """
			using Microsoft.EntityFrameworkCore;
			using Microsoft.EntityFrameworkCore.Metadata.Builders;
			using Norse.Persistence.EntityFramework;

			sealed class Tier1Entity : NorseEntityBase<Tier1Entity>, INorseEntity<Tier1Entity>
			{
				public static void Configure(EntityTypeBuilder<Tier1Entity> builder) { }
			}

			partial class MyContext(DbContextOptions<MyContext> options) : NorseDbContext(options);
			""";

		var compilation = CreateCompilation(source);
		var generator = new EntityConfigurationApplicationGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		var generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
		generated.ShouldContain("partial class MyContext");
		generated.ShouldContain("ConfigureNorseEntities");
	}

	[Fact]
	void Generator_emits_namespaced_Tier1_partial_override_as_valid_C_sharp()
	{
		var source = """
			using Microsoft.EntityFrameworkCore;
			using Microsoft.EntityFrameworkCore.Metadata.Builders;
			using Norse.Persistence.EntityFramework;

			namespace Foo.Bar
			{
				sealed class Tier1Entity : NorseEntityBase<Tier1Entity>, INorseEntity<Tier1Entity>
				{
					public static void Configure(EntityTypeBuilder<Tier1Entity> builder) { }
				}

				partial class MyContext(DbContextOptions<MyContext> options) : NorseDbContext(options);
			}
			""";

		var compilation = CreateCompilation(source);
		var generator = new EntityConfigurationApplicationGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		var generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
		generated.ShouldContain("namespace Foo.Bar");
		generated.ShouldContain("partial class MyContext");
		generated.ShouldContain("ConfigureNorseEntities");

		diagnostics.ShouldBeEmpty();

		foreach (var tree in result.GeneratedTrees)
		{
			var parsed = CSharpSyntaxTree.ParseText(tree.ToString(), cancellationToken: TestContext.Current.CancellationToken);
			parsed.GetDiagnostics(TestContext.Current.CancellationToken).ShouldBeEmpty();
		}

		outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken).ShouldBeEmpty();
	}

	[Fact]
	void Generator_emits_no_source_when_no_entities_found()
	{
		var compilation = CreateCompilation("// empty");
		var generator = new EntityConfigurationApplicationGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		result.GeneratedTrees.ShouldBeEmpty();
	}

	[Fact]
	void Generator_skips_abstract_and_generic_candidates()
	{
		var source = """
			using Microsoft.EntityFrameworkCore.Metadata.Builders;
			using Norse.Persistence.EntityFramework;

			abstract class AbstractEntity : NorseEntityBase<AbstractEntity>, INorseEntity<AbstractEntity>
			{
				public static void Configure(EntityTypeBuilder<AbstractEntity> builder) { }
			}
			""";

		var compilation = CreateCompilation(source);
		var generator = new EntityConfigurationApplicationGenerator();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, TestContext.Current.CancellationToken);
		var result = driver.GetRunResult();

		result.GeneratedTrees.ShouldBeEmpty();
	}

	static Compilation CreateCompilation(string source)
	{
		var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
		var references = new[]
		{
			typeof(object),
			typeof(Norse.Persistence.EntityFramework.INorseEntity<>),
			typeof(Microsoft.EntityFrameworkCore.DbContext),
			typeof(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<>),
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
