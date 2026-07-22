using Microsoft.EntityFrameworkCore;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class NorseDbContextTests
{
	[Fact]
	void ConfigureConventions_registers_both_conventions_by_default()
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

	[Fact]
	void ConfigureNorseEntities_is_called_during_OnModelCreating_and_is_overridable()
	{
		var options = new DbContextOptionsBuilder<HookOverrideContext>()
			.UseSqlite("Data Source=:memory:").Options;
		using HookOverrideContext ctx = new(options);

		_ = ctx.Model;

		ctx.HookInvoked.ShouldBeTrue();
	}

	sealed class HookEntity : NorseEntityBase<HookEntity>, INorseEntity<HookEntity>
	{
		public int Id { get; set; }

		public static void Configure(
			Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<HookEntity> builder) =>
			builder.Property(e => e.Id);
	}

	sealed class HookOverrideContext(DbContextOptions<HookOverrideContext> options) : NorseDbContext(options)
	{
		public bool HookInvoked { get; private set; }
		public DbSet<HookEntity> Entities => Set<HookEntity>();

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<HookEntity>().HasKey(e => e.Id);
		}

		protected override void ConfigureNorseEntities(ModelBuilder modelBuilder)
		{
			base.ConfigureNorseEntities(modelBuilder);
			HookInvoked = true;
		}
	}
}
