namespace Norse.Persistence.EntityFramework.Tests;

public sealed class SnakeCaseNameRewriterTests
{
	[Fact]
	void PascalCase_multi_word_name_gets_underscore_at_each_word_boundary()
	{
		SnakeCaseNameRewriter.RewriteName("CustomerId").ShouldBe("customer_id");
	}

	[Fact]
	void CamelCase_name_gets_underscore_at_each_word_boundary()
	{
		SnakeCaseNameRewriter.RewriteName("customerId").ShouldBe("customer_id");
	}

	[Fact]
	void Acronym_run_at_start_of_name_has_no_leading_or_internal_underscore()
	{
		SnakeCaseNameRewriter.RewriteName("ID").ShouldBe("id");
	}

	[Fact]
	void Acronym_run_followed_by_a_word_splits_before_the_last_acronym_letter()
	{
		SnakeCaseNameRewriter.RewriteName("HTTPClient").ShouldBe("http_client");
	}

	[Fact]
	void Digit_immediately_followed_by_uppercase_does_not_insert_an_underscore()
	{
		SnakeCaseNameRewriter.RewriteName("Value2Text").ShouldBe("value2text");
	}

	[Fact]
	void Pre_existing_underscores_are_preserved_and_reset_word_boundary_state()
	{
		SnakeCaseNameRewriter.RewriteName("already_snake").ShouldBe("already_snake");
	}

	[Fact]
	void Empty_string_returns_empty_string()
	{
		SnakeCaseNameRewriter.RewriteName("").ShouldBe("");
	}

	[Fact]
	void Single_uppercase_letter_lowercases_without_a_leading_underscore()
	{
		SnakeCaseNameRewriter.RewriteName("A").ShouldBe("a");
	}

	[Fact]
	void AspNetIdentity_UserNameIndex_splits_at_every_word_boundary()
	{
		SnakeCaseNameRewriter.RewriteName("UserNameIndex").ShouldBe("user_name_index");
	}

	[Fact]
	void AspNetIdentity_EmailIndex_splits_at_the_word_boundary()
	{
		SnakeCaseNameRewriter.RewriteName("EmailIndex").ShouldBe("email_index");
	}

	[Fact]
	void AspNetIdentity_RoleNameIndex_splits_at_every_word_boundary()
	{
		SnakeCaseNameRewriter.RewriteName("RoleNameIndex").ShouldBe("role_name_index");
	}
}
