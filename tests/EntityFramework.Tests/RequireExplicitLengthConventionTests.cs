using System.ComponentModel.DataAnnotations;

namespace Norse.EntityFramework.Tests;

public sealed class RequireExplicitLengthConventionTests
{
	[Fact]
	public void MaxLengthAttribute_carries_length()
	{
		MaxLengthAttribute attr = new(25);

		attr.Length.ShouldBe(25);
		attr.ShouldBeAssignableTo<MaxLengthAttribute>();
	}

	[Fact]
	public void FixedLengthAttribute_carries_length()
	{
		FixedLengthAttribute attr = new(10);

		attr.Length.ShouldBe(10);
	}

	[Fact]
	public void UnboundedLengthAttribute_carries_negative_one()
	{
		UnboundedLengthAttribute attr = new();

		attr.Length.ShouldBe(-1);
	}
}
