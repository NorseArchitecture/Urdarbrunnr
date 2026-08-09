using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework;

/// <summary>Expects and produces <see cref="GuidByteOrder.Rfc9562" /> — every provider except SQL Server.</summary>
sealed class Rfc9562SequentialGuidValueConverter() : SequentialGuidValueConverter(GuidByteOrder.Rfc9562);
