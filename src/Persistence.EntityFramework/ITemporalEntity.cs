namespace Norse.Persistence.EntityFramework;

/// <summary>
///     Opts an entity into system-time temporality (Norns Model B): the entity's main table gets a
///     database-owned <c>system_period</c>, a history table, versioning triggers, and a timeline view
///     on PostgreSQL, and native system-versioning on SQL Server. Split-table fragments are
///     deliberately not temporal. The period never appears on the CLR type or any payload.
/// </summary>
public interface ITemporalEntity;
