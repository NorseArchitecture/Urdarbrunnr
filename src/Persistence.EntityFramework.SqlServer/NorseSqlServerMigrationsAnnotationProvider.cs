using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.SqlServer.Migrations.Internal;

namespace Norse.Persistence.EntityFramework.SqlServer;

// EF1001: SqlServerMigrationsAnnotationProvider is EF-internal by attribute, but deriving from the provider's
// own migrations-annotation provider is the only supported way to add a drop-side annotation without
// discarding SQL Server's own (which carry EF-native temporality onto the drop).
#pragma warning disable EF1001

/// <summary>
///     Guarantees <see cref="NorseAnnotationNames.TemporalPrincipalTableOnly" /> on the operation that drops an
///     opted-in principal table. A different seam from <see cref="NorseSqlServerAnnotationProvider" />: the
///     relational annotation provider decorates the <em>model</em>'s tables and EF's differ copies those onto
///     create and alter operations itself, but a <c>DropTableOperation</c> takes its annotations from
///     <see cref="IMigrationsAnnotationProvider.ForRemove(ITable)" /> alone, and the dropped entity is gone from
///     the target model. Unlike Npgsql's, SQL Server's own <c>ForRemove(ITable)</c> currently forwards every
///     annotation the table carries (measured against EF 11 rc.2 — the stamp arrives through <c>base</c>), so
///     this override adds the stamp only when the base did not. It is kept so the drop's contract does not rest
///     on an EF-internal implementation detail: were the base ever narrowed to its own temporal annotations,
///     the drop would reach <see cref="NorseSqlServerMigrationsSqlGenerator" /> unmarked and SQL Server would
///     refuse to drop the still-versioned table (13552).
/// </summary>
/// <param name="dependencies">The migrations annotation-provider dependencies, forwarded to SQL Server's own provider.</param>
sealed class NorseSqlServerMigrationsAnnotationProvider(MigrationsAnnotationProviderDependencies dependencies)
	: SqlServerMigrationsAnnotationProvider(dependencies)
{
	/// <inheritdoc />
	public override IEnumerable<IAnnotation> ForRemove(ITable table)
	{
		var forwarded = false;
		foreach (var annotation in base.ForRemove(table))
		{
			forwarded |= annotation.Name == NorseAnnotationNames.TemporalPrincipalTableOnly;
			yield return annotation;
		}

		// The source table already carries the stamp: NorseSqlServerAnnotationProvider put it there when the
		// source model's relational tables were built. Forwarded verbatim rather than re-derived.
		if (!forwarded && table.FindAnnotation(NorseAnnotationNames.TemporalPrincipalTableOnly) is { } stamp)
			yield return stamp;
	}
}
