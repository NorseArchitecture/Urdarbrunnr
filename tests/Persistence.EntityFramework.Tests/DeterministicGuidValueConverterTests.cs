using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class DeterministicGuidValueConverterTests
{
	[Fact]
	void Converts_to_the_underlying_Guid_value()
	{
		DeterministicGuid id = new(DeterministicGuid.Namespaces.Dns, "example.norsearchitecture.dev");
		DeterministicGuidValueConverter converter = new();

		var result = converter.ConvertToProvider(id);

		result.ShouldBe(id.Value);
	}

	[Fact]
	void Converts_from_a_stored_Guid_back_to_the_same_DeterministicGuid()
	{
		DeterministicGuid source = new(DeterministicGuid.Namespaces.Dns, "example.norsearchitecture.dev");
		DeterministicGuidValueConverter converter = new();

		var result = (DeterministicGuid)converter.ConvertFromProvider(source.Value)!;

		result.ShouldBe(source);
	}
}
