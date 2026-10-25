using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Norse.Persistence.EntityFramework;

/// <summary>
///     The provider-neutral realization of
///     <see cref="FragmentCheckConstraintBuilderExtensions.HasFragmentCheckConstraint" />, shared by every provider
///     binding's annotation provider and migrations SQL generator. Public because it is seam surface, as
///     <see cref="NorseAnnotationNames" /> is.
/// </summary>
/// <remarks>
///     EF's own check-constraint machinery is anchored to the entity type's principal table at every step —
///     <c>ICheckConstraint.GetName(StoreObjectIdentifier)</c> resolves no other table, and the differ builds both
///     <c>AddCheckConstraintOperation</c> and <c>DropCheckConstraintOperation</c> from
///     <c>EntityType.GetTableName()</c> — so a fragment's constraint cannot ride it. It rides the fragment's own
///     annotations instead, which EF's snapshot generator already writes inside the fragment's
///     <c>SplitToTable</c>: projected onto the fragment's relational table (<see cref="For" />), the differ sees
///     them, copies them onto the fragment's <c>CreateTableOperation</c>, and surfaces any change as an
///     annotation-only <c>AlterTableOperation</c>; the generator then turns those into EF's own
///     <c>AddCheckConstraintOperation</c>/<c>DropCheckConstraintOperation</c> (<see cref="Expand" />), so each
///     provider emits its native DDL for them.
/// </remarks>
public static class NorseFragmentCheckConstraints
{
	/// <summary>
	///     The fragment check constraints declared on the split fragment that <paramref name="table" /> is, as the
	///     annotations a provider's relational annotation provider projects onto it. Empty for a principal table and
	///     for any table no fragment maps, so a constraint lands on its own fragment and nowhere else.
	/// </summary>
	/// <param name="table">The relational table being annotated.</param>
	/// <returns>One annotation per constraint, under its <see cref="NorseAnnotationNames.FragmentCheckConstraintPrefix" /> name.</returns>
	public static IEnumerable<IAnnotation> For(ITable table)
	{
		var storeObject = StoreObjectIdentifier.Table(table.Name, table.Schema);
		return table.EntityTypeMappings
			.Select(mapping => mapping.TypeBase)
			.OfType<IEntityType>()
			.Select(entityType => entityType.FindMappingFragment(storeObject))
			.OfType<IEntityTypeMappingFragment>()
			.SelectMany(fragment => fragment.GetAnnotations())
			.Where(annotation => IsFragmentCheckConstraint(annotation.Name))
			.DistinctBy(annotation => annotation.Name)
			.Select(annotation => new Annotation(annotation.Name, annotation.Value));
	}

	/// <summary>
	///     A migration batch with its fragment check constraints realized as EF's own check-constraint operations,
	///     in the positions EF's differ itself gives them. Every drop leads the batch — ahead of any column drop that
	///     PostgreSQL would otherwise let take the constraint with it (and SQL Server would refuse, 5074) — and is
	///     addressed by the name its table has at that point, which is the pre-rename name when the batch also
	///     renames the table. Every add trails the batch, after the columns it may name exist. A changed expression
	///     is a drop and an add. The operations given are left untouched; a batch with no fragment check constraint
	///     is returned as it came.
	/// </summary>
	/// <param name="operations">The batch handed to the migrations SQL generator.</param>
	/// <returns>The batch to generate SQL for.</returns>
	public static IReadOnlyList<MigrationOperation> Expand(IReadOnlyList<MigrationOperation> operations)
	{
		List<MigrationOperation>
			drops = [],
			adds = [];
		foreach (var operation in operations)
		{
			switch (operation)
			{
				case CreateTableOperation create:
					adds.AddRange(Of(create).Select(constraint => Add(constraint, create.Name, create.Schema)));
					break;
				case AlterTableOperation alter:
					var (target, source) = (Of(alter), Of(alter.OldTable));
					var (sourceSchema, sourceName) = SourceNameOf(operations, alter.Schema, alter.Name);
					drops.AddRange(source
						.Where(constraint => !target.Contains(constraint))
						.Select(constraint => new DropCheckConstraintOperation
						{
							Name = constraint.Name,
							Table = sourceName,
							Schema = sourceSchema
						}));
					adds.AddRange(target
						.Where(constraint => !source.Contains(constraint))
						.Select(constraint => Add(constraint, alter.Name, alter.Schema)));
					break;
				default:
					continue;
			}
		}

		return drops.Count is 0 && adds.Count is 0 ?
			operations :
			[.. drops, .. operations, .. adds];
	}

	static AddCheckConstraintOperation Add((string Name, string Sql) constraint, string table, string? schema) =>
		new() { Name = constraint.Name, Sql = constraint.Sql, Table = table, Schema = schema };

	// Read off the operation, never the model, so a scaffolded migration — whose C# carries these as .Annotation and
	// .OldAnnotation calls — replays exactly what the differ saw.
	static HashSet<(string Name, string Sql)> Of(IReadOnlyAnnotatable annotatable) =>
	[
		.. annotatable.GetAnnotations()
			.Where(annotation => IsFragmentCheckConstraint(annotation.Name))
			.Select(annotation => (annotation.Name[NorseAnnotationNames.FragmentCheckConstraintPrefix.Length..],
				annotation.Value as string ?? throw new InvalidOperationException(
					$"Fragment check constraint annotation '{annotation.Name}' carries no SQL.")))
	];

	// An AlterTableOperation carries the table's target name; EF sorts a table's rename ahead of it, so a drop led to
	// the front of the batch has to address the table by the name it still has there.
	static (string? Schema, string Name) SourceNameOf(IReadOnlyList<MigrationOperation> operations, string? schema,
		string name) =>
		operations
			.OfType<RenameTableOperation>()
			.Where(rename => (rename.NewName ?? rename.Name) == name && rename.NewSchema == schema)
			.Select(rename => (rename.Schema, rename.Name))
			.FirstOrDefault((schema, name));

	static bool IsFragmentCheckConstraint(string annotationName) =>
		annotationName.StartsWith(NorseAnnotationNames.FragmentCheckConstraintPrefix, StringComparison.Ordinal);
}
