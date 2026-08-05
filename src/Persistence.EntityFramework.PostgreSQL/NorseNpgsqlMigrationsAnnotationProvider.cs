using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Internal;

namespace Norse.Persistence.EntityFramework.PostgreSQL;

// EF1001: NpgsqlMigrationsAnnotationProvider is EF-internal by attribute, but deriving from the
// provider's own migrations-annotation provider is the only supported way to add a drop-side annotation
// without discarding Npgsql's own.
#pragma warning disable EF1001

/// <summary>
/// Carries the <see cref="NorseAnnotationNames.Temporal"/> marker onto the operation that drops a
/// temporal table. This is a different seam from
/// <see cref="NorseNpgsqlAnnotationProvider"/>: the relational annotation provider decorates the
/// <em>model</em>'s tables, and EF's differ copies those annotations onto create and alter operations
/// itself — but a <c>DropTableOperation</c> takes its annotations from
/// <see cref="IMigrationsAnnotationProvider.ForRemove(ITable)"/> alone, whose base implementation
/// returns nothing. Without this override the drop operation arrives at the SQL generator carrying no
/// annotations at all (measured, not assumed), and since the dropped entity is gone from the target
/// model there is nothing left to consult — the table's temporality would be unknowable and the
/// apparatus would outlive it.
/// </summary>
/// <param name="dependencies">The migrations annotation-provider dependencies, forwarded to Npgsql's own provider.</param>
sealed class NorseNpgsqlMigrationsAnnotationProvider(MigrationsAnnotationProviderDependencies dependencies)
	: NpgsqlMigrationsAnnotationProvider(dependencies)
{
	/// <inheritdoc />
	public override IEnumerable<IAnnotation> ForRemove(ITable table)
	{
		foreach (var annotation in base.ForRemove(table))
			yield return annotation;

		// The source table already carries the marker: NorseNpgsqlAnnotationProvider put it there when the
		// source model's relational tables were built. Forwarded verbatim rather than re-derived.
		if (table.FindAnnotation(NorseAnnotationNames.Temporal) is { } temporal)
			yield return temporal;
	}
}
