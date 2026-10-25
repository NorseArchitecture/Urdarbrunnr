namespace Norse.Persistence.EntityFramework;

/// <summary>
///     The Norse model annotation names. Public because they are seam surface: provider binding
///     assemblies (realization hooks, migration SQL generators) read them off the model, the same way
///     EF's own <c>RelationalAnnotationNames</c> is public for its providers.
/// </summary>
public static class NorseAnnotationNames
{
	/// <summary>Stamped <see langword="true" /> on every validated <see cref="ITemporalEntity" /> at model finalize.</summary>
	public const string Temporal = "Norse:Temporal";

	/// <summary>
	///     Stamped by <see cref="TemporalEntityTypeBuilderExtensions.TemporalOnPrincipalTableOnly" /> on a split
	///     temporal entity whose principal table alone is versioned.
	/// </summary>
	public const string TemporalPrincipalTableOnly = "Norse:TemporalPrincipalTableOnly";

	/// <summary>
	///     The prefix of the annotations stamped by
	///     <see cref="FragmentCheckConstraintBuilderExtensions.HasFragmentCheckConstraint" /> on a split
	///     fragment: one annotation per constraint, named this prefix followed by the constraint's database name,
	///     its value the constraint's SQL.
	/// </summary>
	public const string FragmentCheckConstraintPrefix = "Norse:FragmentCheckConstraint:";
}
