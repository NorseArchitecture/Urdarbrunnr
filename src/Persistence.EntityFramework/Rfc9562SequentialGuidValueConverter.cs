using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework;

/// <summary>Expects and produces <see cref="GuidByteOrder.Rfc9562"/> — every provider except SQL Server.</summary>
sealed class Rfc9562SequentialGuidValueConverter(ConverterMappingHints? mappingHints = null) :
	SequentialGuidValueConverter(GuidByteOrder.Rfc9562, mappingHints);
