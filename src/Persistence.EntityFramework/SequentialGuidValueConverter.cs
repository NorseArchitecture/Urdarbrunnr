using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Converts <see cref="SequentialGuid"/> to and from a stored <see cref="Guid"/>, refusing to
/// convert a value whose <see cref="SequentialGuid.Order"/> doesn't match the destination
/// provider's expected byte order. Never reshuffles: SQL Server's <c>uniqueidentifier</c> sort
/// order disagrees with RFC 9562's own byte order, and silently "fixing" a mismatched value would
/// make debugging which byte order a stored GUID is actually in a nightmare. Callers must call
/// <see cref="SequentialGuid.ToSqlOrder"/>/<see cref="SequentialGuid.ToRfcOrder"/> explicitly before
/// assigning a value bound for the other provider.
/// </summary>
abstract class SequentialGuidValueConverter(GuidByteOrder expectedOrder) :
	ValueConverter<SequentialGuid, Guid>(
		guid => Guard(guid, expectedOrder),
		value => new SequentialGuid(value, expectedOrder))
{
	static Guid Guard(SequentialGuid guid, GuidByteOrder expectedOrder)
	{
		if (guid.Order == expectedOrder)
			return guid.Value;

		if (guid.Order == GuidByteOrder.Unspecified)
			throw new InvalidOperationException(
				"SequentialGuid is default (uninitialized) — assign a real value via `new SequentialGuid()` " +
				"or by wrapping an existing one, not a default/unset property.");

		throw new InvalidOperationException(
			$"SequentialGuid is in {guid.Order} byte order but this provider requires {expectedOrder}. " +
			$"Call {(expectedOrder == GuidByteOrder.SqlServer ? "ToSqlOrder()" : "ToRfcOrder()")} explicitly " +
			"before assigning — this converter never silently reshuffles.");
	}
}
