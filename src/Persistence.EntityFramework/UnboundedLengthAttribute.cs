namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Marks a string or binary property as explicitly unbounded — <c>nvarchar(max)</c>/<c>text</c>,
/// <c>varbinary(max)</c>/<c>bytea</c>. Passes EF Core's own <c>-1</c> sentinel for "no maximum."
/// The only attribute-path escape hatch from <see cref="RequireExplicitLengthConvention"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class UnboundedLengthAttribute()
	: System.ComponentModel.DataAnnotations.MaxLengthAttribute(-1);
