namespace Norse.Persistence.EntityFramework;

/// <summary>
/// The Norse model annotation names. Public because they are seam surface: provider binding
/// assemblies (realization hooks, migration SQL generators) read them off the model, the same way
/// EF's own <c>RelationalAnnotationNames</c> is public for its providers.
/// </summary>
public static class NorseAnnotationNames
{
	/// <summary>Stamped <see langword="true"/> on every validated <see cref="ITemporalEntity"/> at model finalize.</summary>
	public const string Temporal = "Norse:Temporal";

	/// <summary>Stamped by <see cref="TemporalEntityTypeBuilderExtensions.TemporalParkedOnSqlServer"/> to acknowledge the SQL-Server-only park.</summary>
	public const string TemporalParkedOnSqlServer = "Norse:TemporalParkedOnSqlServer";
}
