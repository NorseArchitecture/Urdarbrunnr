using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Persistence.EntityFramework.SqlServer.Tests;

// EF1001: the design-time service registration (DesignTimeServiceCollectionExtensions), the migrations
// code-generator selector, and the snapshot model processor the scaffolder's own dependencies carry are EF-internal
// by attribute. They are exactly what
// `dotnet ef migrations add` runs on the snapshot it scaffolded last time, and this helper exists to measure that
// path rather than a reimplementation of it.
#pragma warning disable EF1001

/// <summary>
///     A context's model snapshot, the whole way round: EF's real snapshot generator writes the C#, Roslyn compiles
///     it (Roslyn arrives with EF's own design package), and the compiled <see cref="ModelSnapshot" /> is processed
///     exactly as <c>dotnet ef migrations add</c> processes the snapshot on disk before diffing it against the
///     current model. Nothing is replayed by hand, so whatever the snapshot drops, this drops too.
/// </summary>
static class CompiledSnapshot
{
	/// <summary>The snapshot's C#, as <c>dotnet ef migrations add</c> would write it for <paramref name="context" />.</summary>
	public static string Code(DbContext context, IDesignTimeServices providerServices)
	{
		using var services = DesignTimeServices(context, providerServices);
		return services.GetRequiredService<IMigrationsCodeGeneratorSelector>().Select(language: null)
			.GenerateSnapshot("Norse.Test.Migrations", context.GetType(), "FragmentModelSnapshot",
				context.GetService<IDesignTimeModel>().Model, "20261025000000_Fragment");
	}

	/// <summary>
	///     The model the next <c>migrations add</c> diffs from: <paramref name="code" /> compiled, built, and run
	///     through EF's snapshot model processor.
	/// </summary>
	public static IModel Model(DbContext context, IDesignTimeServices providerServices, string code)
	{
		using var services = DesignTimeServices(context, providerServices);
		var snapshot = Activator.CreateInstance(Compile(code).GetTypes().Single(type => type.IsSubclassOf(typeof(ModelSnapshot))))
			as ModelSnapshot ?? throw new InvalidOperationException("The compiled snapshot did not construct.");
		return services.GetRequiredService<MigrationsScaffolderDependencies>().SnapshotModelProcessor.Process(snapshot.Model)
			?? throw new InvalidOperationException("The snapshot model processor returned no model.");
	}

	// The design-time service provider `dotnet ef` builds for itself: EF's own design-time services, the
	// context's, and the provider's.
	static ServiceProvider DesignTimeServices(DbContext context, IDesignTimeServices providerServices)
	{
		ServiceCollection services = new();
		services.AddEntityFrameworkDesignTimeServices();
		services.AddDbContextDesignTimeServices(context);
		providerServices.ConfigureDesignTimeServices(services);
		return services.BuildServiceProvider();
	}

	static Assembly Compile(string code)
	{
		// The [DbContext(typeof(...))] attribute names the test's own internal context type, which a separate
		// compilation cannot see. It is the snapshot's discovery metadata only; the model is built without it.
		var source = string.Join('\n', code.Split('\n')
			.Where(line => !line.TrimStart().StartsWith("[DbContext(", StringComparison.Ordinal)));
		var compilation = CSharpCompilation.Create($"FragmentSnapshot{Guid.NewGuid():N}",
			[CSharpSyntaxTree.ParseText(source)], References(),
			new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
		using MemoryStream image = new();
		var emitted = compilation.Emit(image);
		emitted.Success.ShouldBeTrue(string.Join(Environment.NewLine,
			emitted.Diagnostics.Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error)));
		image.Position = 0;
		return new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true).LoadFromStream(image);
	}

	// The runtime's own reference set plus every assembly this test process has loaded (EF, the provider).
	static IEnumerable<MetadataReference> References() =>
		(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
		.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
		.Concat(AppDomain.CurrentDomain.GetAssemblies()
			.Where(assembly => !assembly.IsDynamic && assembly.Location.Length > 0)
			.Select(assembly => assembly.Location))
		.Distinct(StringComparer.Ordinal)
		.Select(path => MetadataReference.CreateFromFile(path));
}
