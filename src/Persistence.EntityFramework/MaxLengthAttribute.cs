namespace Norse.Persistence.EntityFramework;

/// <summary>
///     Drop-in replacement for <see cref="System.ComponentModel.DataAnnotations.MaxLengthAttribute" />,
///     restricted to properties and fields — matches the restriction EF Core's own
///     <c>PrecisionAttribute</c> uses, which makes omitting the <c>property:</c> target specifier on a
///     positional record parameter a compile error.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MaxLengthAttribute(int length) :
	System.ComponentModel.DataAnnotations.MaxLengthAttribute(length);
