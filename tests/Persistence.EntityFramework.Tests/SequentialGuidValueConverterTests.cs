using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class SequentialGuidValueConverterTests
{
	[Fact]
	void Rfc9562_converter_passes_through_an_Rfc9562_ordered_value()
	{
		SequentialGuid guid = new();
		Rfc9562SequentialGuidValueConverter converter = new();

		var result = converter.ConvertToProvider(guid);

		result.ShouldBe(guid.Value);
	}

	[Fact]
	void Rfc9562_converter_throws_on_a_SqlServer_ordered_value()
	{
		var guid = new SequentialGuid().ToSqlOrder();
		Rfc9562SequentialGuidValueConverter converter = new();

		var act = () => converter.ConvertToProvider(guid);

		var ex = act.ShouldThrow<InvalidOperationException>();
		ex.Message.ShouldContain("SqlServer");
		ex.Message.ShouldContain("Rfc9562");
	}

	[Fact]
	void Rfc9562_converter_tags_a_value_read_from_the_provider_as_Rfc9562()
	{
		SequentialGuid source = new();
		Rfc9562SequentialGuidValueConverter converter = new();

		var result = (SequentialGuid)converter.ConvertFromProvider(source.Value)!;

		result.Order.ShouldBe(GuidByteOrder.Rfc9562);
		result.Value.ShouldBe(source.Value);
	}

	[Fact]
	void SqlServer_converter_passes_through_a_SqlServer_ordered_value()
	{
		var guid = new SequentialGuid().ToSqlOrder();
		SqlServerSequentialGuidValueConverter converter = new();

		var result = converter.ConvertToProvider(guid);

		result.ShouldBe(guid.Value);
	}

	[Fact]
	void SqlServer_converter_throws_on_an_Rfc9562_ordered_value()
	{
		SequentialGuid guid = new();
		SqlServerSequentialGuidValueConverter converter = new();

		var act = () => converter.ConvertToProvider(guid);

		var ex = act.ShouldThrow<InvalidOperationException>();
		ex.Message.ShouldContain("Rfc9562");
		ex.Message.ShouldContain("SqlServer");
	}

	[Fact]
	void SqlServer_converter_tags_a_value_read_from_the_provider_as_SqlServer()
	{
		var source = new SequentialGuid().ToSqlOrder();
		SqlServerSequentialGuidValueConverter converter = new();

		var result = (SequentialGuid)converter.ConvertFromProvider(source.Value)!;

		result.Order.ShouldBe(GuidByteOrder.SqlServer);
		result.Value.ShouldBe(source.Value);
	}

	[Fact]
	void Rfc9562_converter_throws_a_distinct_message_for_a_default_value()
	{
		var guid = default(SequentialGuid);
		Rfc9562SequentialGuidValueConverter converter = new();

		var act = () => converter.ConvertToProvider(guid);

		var ex = act.ShouldThrow<InvalidOperationException>();
		ex.Message.ShouldContain("default");
		ex.Message.ShouldContain("uninitialized");
	}

	[Fact]
	void SqlServer_converter_throws_a_distinct_message_for_a_default_value()
	{
		var guid = default(SequentialGuid);
		SqlServerSequentialGuidValueConverter converter = new();

		var act = () => converter.ConvertToProvider(guid);

		var ex = act.ShouldThrow<InvalidOperationException>();
		ex.Message.ShouldContain("default");
		ex.Message.ShouldContain("uninitialized");
	}
}
