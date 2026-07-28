using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norse.Abstractions.Migrations;
using Norse.Abstractions.Migrations.Seeding;
using Norse.Persistence.EntityFramework.PostgreSQL;
using Norse.Persistence.EntityFramework.SqlServer;

namespace Norse.Persistence.EntityFramework.Migrations.Generator.Tests;

public sealed class MigrationContributorGeneratorTests
{
	const string SnapshotFixture = """

		[DbContext(typeof(TestContext))]
		sealed class TestContextModelSnapshot : ModelSnapshot
		{
			protected override void BuildModel(ModelBuilder modelBuilder)
			{
			}
		}
		""";

	const string ContributorSource = """
		using Norse.Persistence.EntityFramework;
		using Norse.Persistence.EntityFramework.Migrations;
		using Microsoft.EntityFrameworkCore;
		using Microsoft.EntityFrameworkCore.Infrastructure;

		[MigrationConnectionString("test-db")]
		sealed class TestContributor(TestContext ctx) : EfMigrationContributor<TestContext>(ctx)
		{
			public override string Name => "Test";
		}

		sealed class TestContext(DbContextOptions<TestContext> opts) : NorseDbContext(opts);
		""";

	// The real split shape the 2026-07-25 AppHost failure exposed: the context ships in the realm's
	// data assembly, its ModelSnapshot in a per-provider migrations assembly, and the contributor in
	// the migrations service.
	const string ContextAssemblySource = """
		using Microsoft.EntityFrameworkCore;
		using Norse.Persistence.EntityFramework;

		public sealed class TestContext(DbContextOptions<TestContext> opts) : NorseDbContext(opts);
		""";

	const string SnapshotAssemblySource = """
		using Microsoft.EntityFrameworkCore;
		using Microsoft.EntityFrameworkCore.Infrastructure;

		[DbContext(typeof(TestContext))]
		public sealed class TestContextModelSnapshot : ModelSnapshot
		{
			protected override void BuildModel(ModelBuilder modelBuilder)
			{
			}
		}
		""";

	const string ExternalContextContributorSource = """
		using Norse.Persistence.EntityFramework.Migrations;

		[MigrationConnectionString("test-db")]
		sealed class TestContributor(TestContext ctx) : EfMigrationContributor<TestContext>(ctx)
		{
			public override string Name => "Test";
		}
		""";

	[Fact]
	void Generator_emits_the_discovered_provider_binding_and_neutral_choreography()
	{
		var compilation = CreateCompilation(ContributorSource + SnapshotFixture, PostgresBinding());
		var result = Run(compilation);

		result.GeneratedTrees.Length.ShouldBe(1);
		var generated = result.GeneratedTrees[0].ToString();
		generated.ShouldContain("AddNorseMigrationContext");
		generated.ShouldContain("global::Norse.Persistence.EntityFramework.PostgreSQL.NorsePostgresEfProvider.Instance");
		generated.ShouldContain("test-db");
		generated.ShouldContain("AddNorseMigrationsRunner");
		generated.ShouldNotContain("AddNorsePostgresMigrationContext");
	}

	[Fact]
	void Generator_derives_the_migrations_assembly_from_the_snapshots_assembly_not_the_contributors()
	{
		// Only the snapshot's assembly is a correct answer — never the contributor's own.
		var data = CreateReferencedAssembly("TestAssembly.Data", ContextAssemblySource);
		var migrations = CreateReferencedAssembly("TestAssembly.Data.Migrations.PostgreSQL",
			SnapshotAssemblySource, data);

		var compilation = CreateCompilation(ExternalContextContributorSource, PostgresBinding(), data,
			migrations);
		var result = Run(compilation);

		result.GeneratedTrees.Length.ShouldBe(1);
		var generated = result.GeneratedTrees[0].ToString();
		generated.ShouldContain("\"TestAssembly.Data.Migrations.PostgreSQL\"");
		generated.ShouldNotContain("\"TestAssembly\"");
	}

	[Fact]
	void Generator_reports_NORSE030_when_contributors_exist_but_no_provider_binding_is_referenced()
	{
		var compilation = CreateCompilation(ContributorSource + SnapshotFixture);
		var result = Run(compilation);

		var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE030");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		result.GeneratedTrees.ShouldBeEmpty();
	}

	[Fact]
	void Generator_reports_NORSE031_when_two_provider_bindings_are_referenced()
	{
		var compilation = CreateCompilation(ContributorSource + SnapshotFixture, PostgresBinding(),
			SqlServerBinding());
		var result = Run(compilation);

		var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE031");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
		message.ShouldContain("NorsePostgresEfProvider");
		message.ShouldContain("NorseSqlServerEfProvider");
		result.GeneratedTrees.ShouldBeEmpty();
	}

	[Fact]
	void Generator_reports_NORSE032_when_a_context_has_no_ModelSnapshot()
	{
		var compilation = CreateCompilation(ContributorSource, PostgresBinding());
		var result = Run(compilation);

		var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE032");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("TestContext");
		result.GeneratedTrees.ShouldBeEmpty();
	}

	[Fact]
	void Generator_reports_NORSE034_when_two_assemblies_carry_a_ModelSnapshot_for_the_same_context()
	{
		// A migrations service that can see both of a realm's per-provider migrations assemblies has
		// two [DbContext(typeof(TestContext))] snapshots. Picking the first one found would let
		// reference order silently choose the migrations assembly — the same failure family as the
		// production bug this generator exists to kill.
		var data = CreateReferencedAssembly("TestAssembly.Data", ContextAssemblySource);
		var postgresMigrations = CreateReferencedAssembly("TestAssembly.Data.Migrations.PostgreSQL",
			SnapshotAssemblySource, data);
		var sqlServerMigrations = CreateReferencedAssembly("TestAssembly.Data.Migrations.SqlServer",
			SnapshotAssemblySource, data);

		var compilation = CreateCompilation(ExternalContextContributorSource, PostgresBinding(), data,
			postgresMigrations, sqlServerMigrations);
		var result = Run(compilation);

		var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE034");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
		message.ShouldContain("TestContext");
		message.ShouldContain("TestAssembly.Data.Migrations.PostgreSQL");
		message.ShouldContain("TestAssembly.Data.Migrations.SqlServer");
		result.GeneratedTrees.ShouldBeEmpty();
	}

	[Fact]
	void Generator_reports_NORSE033_when_the_binding_has_no_public_static_Instance()
	{
		var instanceless = CreateReferencedAssembly("TestAssembly.Binding", """
			#nullable enable
			using System;
			using Microsoft.EntityFrameworkCore;
			using Microsoft.EntityFrameworkCore.Metadata;
			using Microsoft.Extensions.Hosting;
			using Norse.Persistence.EntityFramework;

			public sealed class InstancelessEfProvider : INorseEfMigrationProvider
			{
				public void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString,
					string? migrationsAssemblyName)
				{
				}

				public void Enrich<TContext>(IHostApplicationBuilder builder)
					where TContext : DbContext, INorseDbContext
				{
				}

				public Func<string, string>? NameRewriter => null;

				public Action<IConventionEntityType, Func<string, string>>? EntityRenameHook => null;

				public string DesignTimePlaceholderConnectionString(string databaseName) => "";
			}
			""");

		var compilation = CreateCompilation(ContributorSource + SnapshotFixture, instanceless);
		var result = Run(compilation);

		var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
		diagnostic.Id.ShouldBe("NORSE033");
		diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
		diagnostic.GetMessage(CultureInfo.InvariantCulture).ShouldContain("InstancelessEfProvider");
		result.GeneratedTrees.ShouldBeEmpty();
	}

	[Fact]
	void Generator_emits_no_source_and_no_diagnostics_for_an_empty_compilation()
	{
		// The neutral packages themselves compile under this generator in dev mode — a compilation
		// with nothing to wire must not demand a provider binding.
		var compilation = CreateCompilation("// empty");
		var result = Run(compilation);

		result.GeneratedTrees.ShouldBeEmpty();
		result.Diagnostics.ShouldBeEmpty();
	}

	[Fact]
	void Generator_discovers_seed_contributors_and_emits_registration()
	{
		// Seed-only: no migration contributors, therefore no provider binding referenced, and the
		// generator must still emit.
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
		var result = Run(compilation);

		result.Diagnostics.ShouldBeEmpty();
		result.GeneratedTrees.Length.ShouldBe(1);
		var generated = result.GeneratedTrees[0].ToString();
		generated.ShouldContain("ConfigureSeedContributor<global::TestSeedContributor>(builder.Services);");
		generated.ShouldContain("AddTransient<global::Norse.Abstractions.Migrations.Seeding.ISeedContributor, global::TestSeedContributor>");
		generated.ShouldContain("AddNorseSeedingRunner");
	}

	[Fact]
	void Generator_emitted_source_compiles_for_seed_contributor_that_does_not_override_ConfigureServices()
	{
		// ISeedContributor.ConfigureServices is `static virtual void ConfigureServices(...)` — a no-op
		// default. A concrete type that relies on that default has no `ConfigureServices` member
		// reachable via its own type name, so a direct static call fails with CS0117; static virtual
		// interface members are only invocable via the interface name or a constrained type parameter.
		// Every other test here only proves generation *ran* — this one proves the emitted text
		// actually compiles, by feeding the real generated tree back through a second compilation
		// alongside the original source (plus a minimal stand-in for Midgard's runner extensions,
		// which Urðarbrunnr cannot reference — it sits below Midgard in the dependency chain).
		const string Source = """
			using System.Threading;
			using System.Threading.Tasks;
			using Norse.Persistence.EntityFramework;
			using Norse.Persistence.EntityFramework.Migrations;
			using Microsoft.EntityFrameworkCore;
			using Microsoft.EntityFrameworkCore.Infrastructure;
			using Microsoft.Extensions.DependencyInjection;
			using Norse.Abstractions.Migrations.Seeding;

			[MigrationConnectionString("test-db")]
			sealed class TestContributor(TestContext ctx) : EfMigrationContributor<TestContext>(ctx)
			{
				public override string Name => "Test";
			}

			sealed class TestContext(DbContextOptions<TestContext> opts) : NorseDbContext(opts);

			[DbContext(typeof(TestContext))]
			sealed class TestContextModelSnapshot : ModelSnapshot
			{
				protected override void BuildModel(ModelBuilder modelBuilder)
				{
				}
			}

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

		var postgres = PostgresBinding();
		var compilation = CreateCompilation(Source, postgres);
		var result = Run(compilation);

		result.GeneratedTrees.Length.ShouldBe(1);

		var recompiled = CSharpCompilation.Create(
			"TestAssembly.Recompiled",
			[
				CSharpSyntaxTree.ParseText(Source, cancellationToken: TestContext.Current.CancellationToken),
				CSharpSyntaxTree.ParseText(InfrastructureStub, cancellationToken: TestContext.Current.CancellationToken),
				result.GeneratedTrees[0],
			],
			[.. StandardReferences, postgres],
			new(OutputKind.DynamicallyLinkedLibrary));

		IList<Diagnostic> errors = [.. recompiled.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error)];

		errors.ShouldBeEmpty();
	}

	static GeneratorDriverRunResult Run(Compilation compilation)
	{
		MigrationContributorGenerator generator = new();
		GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _,
			TestContext.Current.CancellationToken);
		return driver.GetRunResult();
	}

	static Compilation CreateCompilation(string source, params MetadataReference[] extraReferences) =>
		CSharpCompilation.Create(
			"TestAssembly",
			[CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
			[.. StandardReferences, .. extraReferences],
			new(OutputKind.DynamicallyLinkedLibrary));

	static MetadataReference CreateReferencedAssembly(string assemblyName, string source,
		params MetadataReference[] extraReferences)
	{
		var compilation = CSharpCompilation.Create(assemblyName,
			[CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
			[.. StandardReferences, .. extraReferences],
			new(OutputKind.DynamicallyLinkedLibrary));
		using MemoryStream stream = new();
		var emit = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
		emit.Success.ShouldBeTrue(string.Join(Environment.NewLine, emit.Diagnostics));
		stream.Position = 0;
		return MetadataReference.CreateFromStream(stream);
	}

	// Provider bindings are deliberately NOT part of StandardReferences — every diagnostic in the
	// NORSE030/031/033 family is about which bindings a compilation can see, so each test states its
	// own binding set.
	static MetadataReference PostgresBinding() =>
		MetadataReference.CreateFromFile(typeof(NorsePostgresEfProvider).Assembly.Location);

	static MetadataReference SqlServerBinding() =>
		MetadataReference.CreateFromFile(typeof(NorseSqlServerEfProvider).Assembly.Location);

	// Build metadata references from explicit assembly locations — AppDomain.GetAssemblies()
	// is unreliable in .NET 11 due to metadata pre-sharing; typeof().Assembly.Location is stable.
	// In .NET 5+ the public Attribute/Object surface lives in System.Runtime.dll (a facade), not
	// System.Private.CoreLib — both must be present for Roslyn to bind attribute constructors.
	static IList<MetadataReference> StandardReferences { get; } = BuildStandardReferences();

	static IList<MetadataReference> BuildStandardReferences()
	{
		var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
		Type[] anchors =
		[
			typeof(object),
			typeof(MigrationConnectionStringAttribute),
			typeof(NorseDbContext),
			typeof(IMigrationContributor),
			typeof(ISeedContributor),
			typeof(IServiceCollection),
			typeof(DbContext),
			typeof(ModelSnapshot),
			typeof(IHostApplicationBuilder),
		];

		List<MetadataReference> references = [];
		foreach (var location in anchors.Select(t => t.Assembly.Location).Distinct())
			references.Add(MetadataReference.CreateFromFile(location));

		references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
		return references;
	}
}
