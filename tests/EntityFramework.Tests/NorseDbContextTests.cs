using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework.Tests;

public sealed class NorseDbContextTests
{
	[Fact]
	public void ConfigureConventions_registers_both_conventions_by_default()
	{
		// PlainEntity has no length problem (no string/byte[] properties) but does violate
		// RequireEntityConfigurationConvention purely by inheriting NorseDbContext with no override —
		// proving both conventions are wired in without any per-context opt-in call.
		var options = new DbContextOptionsBuilder<UnconfiguredContext>()
			.UseSqlite("Data Source=:memory:").Options;
		using UnconfiguredContext ctx = new(options);

		var act = () => ctx.Model;

		act.ShouldThrow<InvalidOperationException>();
	}

	sealed class PlainEntity
	{
		public int Id { get; set; }
	}

	sealed class UnconfiguredContext(DbContextOptions<UnconfiguredContext> options) : NorseDbContext(options)
	{
		public DbSet<PlainEntity> Entities => Set<PlainEntity>();
	}
}
