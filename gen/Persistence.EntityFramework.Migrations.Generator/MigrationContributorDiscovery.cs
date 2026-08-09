using Microsoft.CodeAnalysis;

namespace Norse.Persistence.EntityFramework.Migrations.Generator;

// Provider-agnostic discovery of EfMigrationContributor<TContext> implementations, ISeedContributor
// implementations, and the compilation's INorseEfMigrationProvider bindings. Roslyn generators can't
// reference other analyzer-only assemblies, so this is plain source compiled into the generator
// assembly, never a shared package.
static class MigrationContributorDiscovery
{
	const string AttributeMetadataName =
		"Norse.Persistence.EntityFramework.Migrations.MigrationConnectionStringAttribute";

	const string ContributorInterfaceMetadataName =
		"Norse.Abstractions.Migrations.IMigrationContributor";

	const string ModelSnapshotMetadataName =
		"Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot";

	const string DbContextAttributeMetadataName =
		"Microsoft.EntityFrameworkCore.Infrastructure.DbContextAttribute";

	const string MigrationProviderInterfaceMetadataName =
		"Norse.Persistence.EntityFramework.INorseEfMigrationProvider";

	const string SeedContributorInterfaceMetadataName =
		"Norse.Abstractions.Migrations.Seeding.ISeedContributor";

	public static IList<ContributorInfo> FindContributors(Compilation compilation)
	{
		IList<ContributorInfo> results = [];
		var snapshotAssemblies = FindSnapshotAssembliesByContext(compilation);

		foreach (var type in AllTypes(compilation))
		{
			if (type.IsAbstract)
				continue;

			if (!ImplementsContributorInterface(type))
				continue;

			var attr = type.GetAttributes()
				.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeMetadataName);

			if (attr is null || attr.ConstructorArguments.Length == 0)
				continue;

			if (attr.ConstructorArguments[0].Value is not string connectionStringName)
				continue;

			var dbContextType = FindEfContextType(type);
			if (dbContextType is null)
				continue;

			var contextDisplay = dbContextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

			results.Add(new ContributorInfo(
				type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				contextDisplay,
				connectionStringName,
				snapshotAssemblies.TryGetValue(contextDisplay, out var assemblies) ?
					assemblies :
					[]));
		}

		return results;
	}

	// Covers both production (contributors in referenced packages) and
	// test scenarios (contributor defined in compilation source trees). Provider bindings and
	// ModelSnapshots only ever live in referenced assemblies, so the referenced-assembly leg is
	// load-bearing for every discovery below, not just an accommodation for packaged contributors.
	static IEnumerable<INamedTypeSymbol> AllTypes(Compilation compilation)
	{
		foreach (var type in compilation.SourceModule.ReferencedAssemblySymbols.SelectMany(assembly =>
			GetAllTypes(assembly.GlobalNamespace)))
			yield return type;

		foreach (var type in GetAllTypes(compilation.Assembly.GlobalNamespace))
			yield return type;
	}

	// Match by metadata name to avoid cross-layer symbol identity issues in the
	// generator's CompilationProvider context.
	static bool ImplementsContributorInterface(INamedTypeSymbol type) =>
		type.AllInterfaces.Any(i => i.ToDisplayString() == ContributorInterfaceMetadataName);

	static INamedTypeSymbol? FindEfContextType(INamedTypeSymbol type)
	{
		var current = type.BaseType;
		while (current is not null)
		{
			if (current.OriginalDefinition.MetadataName == "EfMigrationContributor`1" &&
				current.OriginalDefinition.ContainingNamespace?.ToDisplayString() ==
				"Norse.Persistence.EntityFramework.Migrations" &&
				current.TypeArguments.Length == 1)
			{
				return current.TypeArguments[0] as INamedTypeSymbol;
			}

			current = current.BaseType;
		}

		return null;
	}

	// The 2026-07-25 AppHost failure, fixed structurally: the migrations assembly is wherever the
	// context's ModelSnapshot actually compiles — never the contributor's own assembly, which the
	// shared-contributor/provider-split project shape made wrong. Every match is collected, never
	// just the first: a compilation that can see both a realm's *.Migrations.PostgreSQL and its
	// *.Migrations.SqlServer has two snapshots for one context, and letting reference order pick the
	// winner would be exactly the silent wrong answer this generator exists to kill (NORSE034).
	// One walk of the reference closure for the whole compilation, not one per contributor.
	static Dictionary<string, IList<string>> FindSnapshotAssembliesByContext(Compilation compilation)
	{
		// Default string equality is already ordinal; no comparer to state.
		Dictionary<string, IList<string>> results = [];

		foreach (var type in AllTypes(compilation))
		{
			if (type.IsAbstract || !DerivesFromModelSnapshot(type))
				continue;

			var attr = type.GetAttributes().FirstOrDefault(a =>
				a.AttributeClass?.ToDisplayString() == DbContextAttributeMetadataName);

			// Deliberately not a list pattern: this assembly targets netstandard2.0 (Roslyn's
			// analyzer contract) and System.Index does not exist there.
			if (attr is null || attr.ConstructorArguments.Length != 1)
				continue;

			if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol snapshotContext)
				continue;

			var contextDisplay = snapshotContext.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			if (!results.TryGetValue(contextDisplay, out var assemblies))
				results[contextDisplay] = assemblies = [];

			// Deduplicated by assembly, not by snapshot type: several snapshots for one context inside
			// a single assembly still answer the migrations-assembly question unambiguously.
			var assemblyName = type.ContainingAssembly.Name;
			if (!assemblies.Contains(assemblyName))
				assemblies.Add(assemblyName);
		}

		return results;
	}

	// Display-string matching, not SymbolEqualityComparer -- same cross-layer symbol-identity
	// rationale as ImplementsContributorInterface above.
	static bool DerivesFromModelSnapshot(INamedTypeSymbol type)
	{
		var current = type.BaseType;
		while (current is not null)
		{
			if (current.ToDisplayString() == ModelSnapshotMetadataName)
				return true;
			current = current.BaseType;
		}

		return false;
	}

	public static IList<ProviderInfo> FindMigrationProviders(Compilation compilation)
	{
		IList<ProviderInfo> results = [];

		foreach (var type in AllTypes(compilation))
		{
			// Public-only, matching the Instance check below: a binding the generated code cannot name
			// is not a binding this compilation has, and counting it would either emit an
			// inaccessible `global::X.Instance` (CS0122) or raise a phantom NORSE031.
			if (type.IsAbstract || type.TypeKind != TypeKind.Class ||
				type.DeclaredAccessibility != Accessibility.Public)
				continue;

			if (!type.AllInterfaces.Any(i =>
				i.ToDisplayString() == MigrationProviderInterfaceMetadataName))
				continue;

			var hasInstance = type.GetMembers("Instance").OfType<IPropertySymbol>()
				.Any(p => p.IsStatic && p.DeclaredAccessibility == Accessibility.Public);

			results.Add(new ProviderInfo(
				type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), hasInstance));
		}

		return results;
	}

	static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
	{
		foreach (var type in ns.GetTypeMembers())
			yield return type;

		foreach (var child in ns.GetNamespaceMembers())
			foreach (var type in GetAllTypes(child))
				yield return type;
	}

	public static IList<SeedContributorInfo> FindSeedContributors(Compilation compilation)
	{
		IList<SeedContributorInfo> results = [];

		foreach (var type in AllTypes(compilation))
		{
			if (type.IsAbstract)
				continue;

			if (!ImplementsSeedContributorInterface(type))
				continue;

			results.Add(new SeedContributorInfo(
				type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
		}

		return results;
	}

	static bool ImplementsSeedContributorInterface(INamedTypeSymbol type) =>
		type.AllInterfaces.Any(i => i.ToDisplayString() == SeedContributorInterfaceMetadataName);
}

readonly struct ContributorInfo(
	string contributorType,
	string contextType,
	string connectionStringName,
	IList<string> migrationsAssemblyNames)
{
	public string ContributorType { get; } = contributorType;
	public string ContextType { get; } = contextType;
	public string ConnectionStringName { get; } = connectionStringName;

	// Every assembly carrying a ModelSnapshot for this contributor's context. Zero is NORSE032,
	// more than one is NORSE034; exactly one is the migrations assembly.
	public IList<string> MigrationsAssemblyNames { get; } = migrationsAssemblyNames;
}

readonly struct SeedContributorInfo(string contributorType)
{
	public string ContributorType { get; } = contributorType;
}

readonly struct ProviderInfo(string typeDisplayName, bool hasInstance)
{
	public string TypeDisplayName { get; } = typeDisplayName;
	public bool HasInstance { get; } = hasInstance;
}
