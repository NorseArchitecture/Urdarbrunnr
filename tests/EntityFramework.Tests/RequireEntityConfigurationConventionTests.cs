using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.EntityFramework.Tests;

public sealed class RequireEntityConfigurationConventionTests
{
	[Fact]
	public void Tier1_entity_via_NorseEntityBase_must_implement_Configure()
	{
		typeof(INorseEntity<Tier1Entity>).IsAssignableFrom(typeof(Tier1Entity)).ShouldBeTrue();
	}

	sealed class Tier1Entity : NorseEntityBase<Tier1Entity>, INorseEntity<Tier1Entity>
	{
		public int Id { get; set; }

		public static void Configure(EntityTypeBuilder<Tier1Entity> builder) =>
			builder.Property(e => e.Id);
	}
}
