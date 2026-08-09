using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.Internal;

namespace Norse.Persistence.EntityFramework.PostgreSQL;

// EF1001: NpgsqlAnnotationProvider is EF-internal by attribute, but deriving from the provider's own
// annotation provider is the only supported way to add a relational annotation without discarding
// every Npgsql annotation. The emission-seam spike proved the derivation is required: without it,
// EF's differ suppresses annotation-only marker transitions entirely
// (../Glitnir/poc/ef-temporal-emission/FINDINGS.md).
#pragma warning disable EF1001

/// <summary>
///     Projects the <see cref="NorseAnnotationNames.Temporal" /> marker from the entity type onto its
///     relational table, so EF's model differ can see it. Two things depend on that projection: the
///     enable/disable transitions in the chassis design §3.3, which EF emits as annotation-only table
///     alterations and would otherwise diff away to nothing, and the marker's presence on
///     <c>AlterTableOperation.OldTable</c>, the only discriminator available on the disable side once
///     the entity has left the target model.
/// </summary>
/// <param name="dependencies">The relational annotation-provider dependencies, forwarded to Npgsql's own provider.</param>
sealed class NorseNpgsqlAnnotationProvider(RelationalAnnotationProviderDependencies dependencies)
	: NpgsqlAnnotationProvider(dependencies)
{
	/// <inheritdoc />
	public override IEnumerable<IAnnotation> For(ITable table, bool designTime)
	{
		foreach (var annotation in base.For(table, designTime))
			yield return annotation;

		if (table.EntityTypeMappings.Any(mapping => IsRootTableOfTemporalEntity(mapping.TypeBase, table)))
			yield return new Annotation(NorseAnnotationNames.Temporal, true);
	}

	// A split entity maps to its main table AND to every fragment table; only the main table carries
	// the apparatus (design §2.3), so a fragment must not inherit the marker just by being mapped
	// from a marked entity type.
	static bool IsRootTableOfTemporalEntity(ITypeBase typeBase, ITable table) =>
		typeBase.FindAnnotation(NorseAnnotationNames.Temporal)?.Value as bool? == true
		&& StoreObjectIdentifier.Create(typeBase, StoreObjectType.Table)
		== StoreObjectIdentifier.Table(table.Name, table.Schema);
}
