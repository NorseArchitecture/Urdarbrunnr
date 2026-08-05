using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Registers the model-finalizing conventions and provider-aware value conversions every Norse EF
/// context is guaranteed to enforce: explicit string/byte[] length
/// (<see cref="RequireExplicitLengthConvention"/>), mandatory entity self-configuration
/// (<see cref="RequireEntityConfigurationConvention"/>), temporal entity validation
/// (<see cref="TemporalEntityConvention"/>), and the correct <see cref="SequentialGuid"/>
/// byte-order converter for the destination provider.
/// </summary>
public static class NorseModelConventions
{
	/// <summary>
	/// Adds the Norse model-finalizing conventions, and the provider-correct
	/// <see cref="SequentialGuid"/> converter, to <paramref name="configurationBuilder"/>.
	/// </summary>
	/// <param name="configurationBuilder">The configuration builder to register conventions on.</param>
	/// <param name="applyFixedLength">
	/// Whether <see cref="FixedLengthAttribute"/> should translate to <c>.IsFixedLength()</c>. Pass
	/// <see langword="true"/> only for providers where fixed-length storage has a real benefit (SQL
	/// Server); Postgres and everything else should pass <see langword="false"/> — see
	/// <see cref="FixedLengthAttribute"/>'s remarks for why. No default: every caller states its
	/// provider explicitly rather than silently inheriting a guess.
	/// </param>
	/// <param name="sequentialGuidOrder">
	/// Which <see cref="GuidByteOrder"/> the model-wide <see cref="SequentialGuid"/> converter expects
	/// for this provider — <see cref="GuidByteOrder.SqlServer"/> selects
	/// <see cref="SqlServerSequentialGuidValueConverter"/>, <see cref="GuidByteOrder.Rfc9562"/> selects
	/// <see cref="Rfc9562SequentialGuidValueConverter"/>, and anything else — including
	/// <see cref="GuidByteOrder.Unspecified"/> — throws <see cref="ArgumentOutOfRangeException"/>; there is
	/// no fallback converter. Deliberately independent of
	/// <paramref name="applyFixedLength"/>: both happen to be driven by the same provider check today,
	/// but they are unrelated facts (a general storage-engine question vs. a SQL-Server-specific
	/// comparison quirk) — a future provider could decouple them, and folding both into one flag would
	/// silently break whichever one didn't win. No default, for the same reason as
	/// <paramref name="applyFixedLength"/>.
	/// </param>
	/// <param name="temporalRealizationHook">
	/// The provider binding's <see cref="INorseEfProvider.TemporalRealizationHook"/>, invoked by
	/// <see cref="TemporalEntityConvention"/> once per validated temporal entity immediately after
	/// its <see cref="NorseAnnotationNames.Temporal"/> stamp, or <see langword="null"/> when the
	/// provider realizes temporality elsewhere. No default, for the same reason as the other
	/// parameters: whether realization happens in the model is a provider fact every caller states.
	/// </param>
	/// <returns>The same <paramref name="configurationBuilder"/>, for chaining.</returns>
	public static ModelConfigurationBuilder Apply(ModelConfigurationBuilder configurationBuilder,
		bool applyFixedLength, GuidByteOrder sequentialGuidOrder,
		Action<IConventionEntityType>? temporalRealizationHook)
	{
		configurationBuilder.Conventions.Add(_ => new RequireExplicitLengthConvention(applyFixedLength));
		configurationBuilder.Conventions.Add(static _ => new RequireEntityConfigurationConvention());
		configurationBuilder.Conventions.Add(_ => new TemporalEntityConvention(temporalRealizationHook));
		var converterType = sequentialGuidOrder switch
		{
			GuidByteOrder.SqlServer => typeof(SqlServerSequentialGuidValueConverter),
			GuidByteOrder.Rfc9562 => typeof(Rfc9562SequentialGuidValueConverter),
			_ => throw new ArgumentOutOfRangeException(nameof(sequentialGuidOrder), sequentialGuidOrder,
				"GuidByteOrder.Unspecified (or any other unhandled value) is never a valid argument.")
		};
		configurationBuilder.Properties<SequentialGuid>().HaveConversion(converterType);
		configurationBuilder.Properties<DeterministicGuid>().HaveConversion<DeterministicGuidValueConverter>();
		return configurationBuilder;
	}
}
