using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework;

/// <summary>Temporal opt-outs declared per entity in its static Configure.</summary>
public static class TemporalEntityTypeBuilderExtensions
{
	extension<TEntity>(EntityTypeBuilder<TEntity> builder) where TEntity : class
	{
		/// <summary>
		/// Acknowledges that this split temporal entity is deliberately non-temporal on SQL Server
		/// until dotnet/efcore#26457 ships per-fragment temporal control. Deleted the day upstream moves.
		/// </summary>
		public EntityTypeBuilder<TEntity> TemporalParkedOnSqlServer() =>
			builder.HasAnnotation(NorseAnnotationNames.TemporalParkedOnSqlServer, true);
	}
}
