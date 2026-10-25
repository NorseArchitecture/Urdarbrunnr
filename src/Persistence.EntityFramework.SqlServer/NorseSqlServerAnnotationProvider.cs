using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.SqlServer.Metadata.Internal;

namespace Norse.Persistence.EntityFramework.SqlServer;

// EF1001: SqlServerAnnotationProvider is EF-internal by attribute, but deriving from the provider's own
// annotation provider is the only supported way to add a relational annotation without discarding every
// SQL Server annotation (EF-native temporality, memory optimization, clustering among them). The PostgreSQL
// binding's emission-seam spike proved the derivation is required: without it, EF's differ suppresses
// annotation-only marker transitions entirely (../Glitnir/poc/ef-temporal-emission/FINDINGS.md).
#pragma warning disable EF1001

/// <summary>
///     Projects <see cref="NorseAnnotationNames.TemporalPrincipalTableOnly" /> from an opted-in split temporal
///     entity onto its principal relational table, so EF's model differ can see it — the SQL Server twin of
///     the PostgreSQL binding's annotation provider. The entity is deliberately not EF-temporal (spec §6.1),
///     so nothing EF-native records its temporality on the table; this projection is the only record. Two
///     things depend on it: the create path, which reads it off <c>CreateTableOperation</c>, and the
///     enable/disable transitions, which EF emits as annotation-only table alterations and would otherwise
///     diff away to nothing. EF-native temporal entities are left to EF's own annotations and never carry it.
///     It also projects a split fragment's <see cref="NorseAnnotationNames.FragmentCheckConstraintPrefix" />
///     annotations onto that fragment's table alone (<see cref="NorseFragmentCheckConstraints.For" />), the only
///     way a fragment's check constraint reaches the differ.
/// </summary>
/// <param name="dependencies">The relational annotation-provider dependencies, forwarded to SQL Server's own provider.</param>
sealed class NorseSqlServerAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
	: SqlServerAnnotationProvider(dependencies)
{
	/// <inheritdoc />
	public override IEnumerable<IAnnotation> For(ITable table, bool designTime)
	{
		foreach (var annotation in base.For(table, designTime))
			yield return annotation;

		if (table.EntityTypeMappings.Any(mapping => IsPrincipalTableOfOptedInEntity(mapping.TypeBase, table)))
			yield return new Annotation(NorseAnnotationNames.TemporalPrincipalTableOnly, true);

		// Design time only, as EF's own check constraints are: they matter to migrations alone.
		if (!designTime)
			yield break;
		foreach (var constraint in NorseFragmentCheckConstraints.For(table))
			yield return constraint;
	}

	// A split entity maps to its principal table AND to every fragment table; only the principal is
	// versioned (spec §6.1), so a fragment must not inherit the stamp just by being mapped from the entity.
	static bool IsPrincipalTableOfOptedInEntity(ITypeBase typeBase, ITable table) =>
		typeBase.FindAnnotation(NorseAnnotationNames.Temporal)?.Value as bool? == true
		&& typeBase.FindAnnotation(NorseAnnotationNames.TemporalPrincipalTableOnly)?.Value as bool? == true
		&& StoreObjectIdentifier.Create(typeBase, StoreObjectType.Table)
		== StoreObjectIdentifier.Table(table.Name, table.Schema);
}
