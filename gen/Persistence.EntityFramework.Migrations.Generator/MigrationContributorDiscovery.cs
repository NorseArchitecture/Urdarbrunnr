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

	public static IList<ContributorInfo> FindContributors(Compilation compilation)
	{
		IList<ContributorInfo> results = [];

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

			if (attr.ConstructorArguments[0].Value is not String connectionStringName)
				continue;

			var dbContextType = FindEfContextType(type);
			if (dbContextType is null)
				continue;

			results.Add(new ContributorInfo(
				type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				dbContextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				connectionStringName,
				FindMigrationsAssemblyName(compilation, dbContextType)));
		}

		return results;
	}

	// Covers both production (contributors in referenced packages) and
	// test scenarios (contributor defined in compilation source trees). Provider bindings and
	// ModelSnapshots only ever live in referenced assemblies, so the referenced-assembly leg is
	// load-bearing for every discovery below, not just an accommodation for packaged contributors.
	static IEnumerable<INamedTypeSymbol> AllTypes(Compilation compilation)
	{
		foreach (var type in compilation.SourceModule.ReferencedAssemblySymbols.SelectMany(assembly => GetAllTypes(assembly.GlobalNamespace)))
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
				current.OriginalDefinition.ContainingNamespace?.ToDisplayString() == "Norse.Persistence.EntityFramework.Migrations" &&
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
	// shared-contributor/provider-split project shape made wrong.
	static string? FindMigrationsAssemblyName(Compilation compilation, INamedTypeSymbol contextType)
	{
		var contextDisplay = contextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

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

			if (attr.ConstructorArguments[0].Value is INamedTypeSymbol snapshotContext &&
				snapshotContext.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == contextDisplay)
				return type.ContainingAssembly.Name;
		}

		return null;
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
			if (type.IsAbstract || type.TypeKind != TypeKind.Class)
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

	const string SeedContributorInterfaceMetadataName =
		"Norse.Abstractions.Migrations.Seeding.ISeedContributor";

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
	string? migrationsAssemblyName)
{
	public string ContributorType { get; } = contributorType;
	public string ContextType { get; } = contextType;
	public string ConnectionStringName { get; } = connectionStringName;
	public string? MigrationsAssemblyName { get; } = migrationsAssemblyName;
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
