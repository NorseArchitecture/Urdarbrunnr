using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework;

/// <summary>
///     Converts <see cref="DeterministicGuid" /> to and from a stored <see cref="Guid" />. Unlike
///     <see cref="SequentialGuidValueConverter" />, there is no provider-specific byte order to guard —
///     <see cref="DeterministicGuid" /> is a pure content hash with no time component and no meaningful
///     sort order (see its own remarks), so this converter is a plain, unconditional round trip.
/// </summary>
sealed class DeterministicGuidValueConverter() :
	ValueConverter<DeterministicGuid, Guid>(
		id => id.Value,
		value => new(value));
