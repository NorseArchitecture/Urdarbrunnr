using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework;

/// <summary>Check constraints declared on an entity-splitting fragment, inside its <c>SplitToTable</c>.</summary>
public static class FragmentCheckConstraintBuilderExtensions
{
	extension<TEntity>(SplitTableBuilder<TEntity> builder) where TEntity : class
	{
		/// <summary>
		///     Declares a <c>CHECK</c> constraint on this split fragment's table. EF Core cannot: a
		///     <see cref="SplitTableBuilder{TEntity}" /> has no <c>HasCheckConstraint</c>, and an entity-level one is
		///     resolved to the principal table alone, so its <c>CREATE TABLE</c> would name columns that live in the
		///     fragment. The constraint is stamped on the fragment's own mapping — so the model snapshot carries it,
		///     and a regenerated migration reproduces it — and both provider bindings emit it on the fragment
		///     table: <c>ADD CONSTRAINT</c> after the table is created or when the constraint is added,
		///     <c>DROP CONSTRAINT</c> when it is removed, and both when its SQL changes. The fragment is never
		///     versioned, so the DDL is plain on every provider. Re-declaring a name replaces its SQL.
		/// </summary>
		/// <param name="name">
		///     The constraint's database name, used verbatim: the naming convention does not rewrite it, exactly as it
		///     leaves EF's own check-constraint names alone.
		/// </param>
		/// <param name="sql">
		///     The constraint's expression, without the surrounding <c>CHECK (…)</c>. Provider-specific, as
		///     <c>HasComputedColumnSql</c> is: it names columns as the provider's naming convention leaves them.
		/// </param>
		/// <returns>The same builder, for chaining.</returns>
		public SplitTableBuilder<TEntity> HasFragmentCheckConstraint(string name, string sql)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(name);
			ArgumentException.ThrowIfNullOrWhiteSpace(sql);
			return builder.HasAnnotation($"{NorseAnnotationNames.FragmentCheckConstraintPrefix}{name}", sql);
		}
	}
}
