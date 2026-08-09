using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework;

/// <summary>
///     Expects and produces <see cref="GuidByteOrder.SqlServer" /> — SQL Server's own <c>uniqueidentifier</c> sort
///     order.
/// </summary>
sealed class SqlServerSequentialGuidValueConverter() : SequentialGuidValueConverter(GuidByteOrder.SqlServer);
