namespace Norse.EntityFramework;

/// <summary>
/// Marks a string property as fixed-length. Equivalent to <c>.HasMaxLength(n).IsFixedLength()</c> —
/// <c>nchar(n)</c>/<c>char(n)</c> depending on provider. <see cref="RequireExplicitLengthConvention"/>
/// translates presence of this attribute into <c>IsFixedLength()</c> at model-finalization time.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class FixedLengthAttribute(int length)
	: System.ComponentModel.DataAnnotations.MaxLengthAttribute(length);
