using Microsoft.CodeAnalysis;

namespace Norse.Persistence.EntityFramework.Design.Generator.Shared;

// Linked into both Persistence.EntityFramework.Design.PostgreSQL.Generator and
// Persistence.EntityFramework.Design.SqlServer.Generator via <Compile Include> -- provider-agnostic
// discovery of EfMigrationContributor<TContext> implementations. Roslyn generators can't reference
// other analyzer-only assemblies, so this is plain shared source (compiled once per consuming
// assembly), not a shared package reference.
static class MigrationContributorDiscovery
{
	const string AttributeMetadataName =
		"Norse.Persistence.EntityFramework.Design.MigrationConnectionStringAttribute";

	const string ContributorInterfaceMetadataName =
		"Norse.Abstractions.Migrations.IMigrationContributor";

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

			var connectionStringName = attr.ConstructorArguments[0].Value as string;
			if (connectionStringName is null)
				continue;

			var dbContextType = FindEfContextType(type);
			if (dbContextType is null)
				continue;

			results.Add(new ContributorInfo(
				type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				dbContextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
				connectionStringName,
				type.ContainingAssembly.Name));
		}

		return results;
	}

	// Covers both production (contributors in referenced packages) and
	// test scenarios (contributor defined in compilation source trees).
	static IEnumerable<INamedTypeSymbol> AllTypes(Compilation compilation)
	{
		foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
			foreach (var type in GetAllTypes(assembly.GlobalNamespace))
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
			if (current.OriginalDefinition?.MetadataName == "EfMigrationContributor`1" &&
				current.OriginalDefinition?.ContainingNamespace?.ToDisplayString() == "Norse.Persistence.EntityFramework.Design" &&
				current.TypeArguments.Length == 1)
			{
				return current.TypeArguments[0] as INamedTypeSymbol;
			}

			current = current.BaseType;
		}

		return null;
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

readonly struct ContributorInfo
{
	public ContributorInfo(
		string contributorType,
		string contextType,
		string connectionStringName,
		string migrationsAssemblyName)
	{
		ContributorType = contributorType;
		ContextType = contextType;
		ConnectionStringName = connectionStringName;
		MigrationsAssemblyName = migrationsAssemblyName;
	}

	public string ContributorType { get; }
	public string ContextType { get; }
	public string ConnectionStringName { get; }
	public string MigrationsAssemblyName { get; }
}

readonly struct SeedContributorInfo
{
	public SeedContributorInfo(string contributorType) => ContributorType = contributorType;

	public string ContributorType { get; }
}
