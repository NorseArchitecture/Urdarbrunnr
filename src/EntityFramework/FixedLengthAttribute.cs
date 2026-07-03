namespace Norse.EntityFramework;

/// <summary>
/// Marks a string property as fixed-length. On SQL Server, this translates to
/// <c>.HasMaxLength(n).IsFixedLength()</c> (<c>nchar(n)</c>/<c>char(n)</c>) via
/// <see cref="RequireExplicitLengthConvention"/> at model-finalization time. On every other
/// provider — Postgres included — this attribute behaves exactly like plain
/// <see cref="MaxLengthAttribute"/>: still bounded, never <c>.IsFixedLength()</c>. Postgres's own
/// documentation states <c>character(n)</c> has no storage or performance advantage over
/// <c>character varying(n)</c> on that engine, and is usually the slower of the two — unlike SQL
/// Server, where fixed-length storage avoids a per-row length-prefix. Use this attribute to record
/// design intent ("this really is fixed-length data") even on providers where it has no storage
/// effect; the provider-specific benefit is applied automatically, never by hand.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class FixedLengthAttribute(int length)
	: System.ComponentModel.DataAnnotations.MaxLengthAttribute(length);
