namespace Norse.Persistence.EntityFramework.Tests;

public sealed class NorseNameRewritersTests
{
	[Theory]
	[InlineData("CountryOrArea", "country_or_area")]
	[InlineData("ISOCode", "iso_code")]
	[InlineData("Alpha2", "alpha2")]
	[InlineData("already_snake", "already_snake")]
	void LowerSnakeCase_matches_the_engine_native_postgres_style(string input, string expected) =>
		NorseNameRewriters.LowerSnakeCase(input).ShouldBe(expected);

	[Theory]
	[InlineData("CountryOrArea", "COUNTRY_OR_AREA")]
	[InlineData("ISOCode", "ISO_CODE")]
	[InlineData("Alpha2", "ALPHA2")]
	[InlineData("already_snake", "ALREADY_SNAKE")]
	void UpperSnakeCase_matches_the_engine_native_oracle_style(string input, string expected) =>
		NorseNameRewriters.UpperSnakeCase(input).ShouldBe(expected);

	[Fact]
	void Upper_and_lower_agree_on_word_boundaries()
	{
		var lower = NorseNameRewriters.LowerSnakeCase("PolicyBoundEvent2Handler");
		var upper = NorseNameRewriters.UpperSnakeCase("PolicyBoundEvent2Handler");
		upper.ShouldBe(lower.ToUpperInvariant());
	}
}
