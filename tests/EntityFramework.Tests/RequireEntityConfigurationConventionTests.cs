using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.EntityFramework.Tests;

public sealed class RequireEntityConfigurationConventionTests
{
	[Fact]
	public void Tier1_entity_via_NorseEntityBase_must_implement_Configure()
	{
		typeof(INorseEntity<Tier1Entity>).IsAssignableFrom(typeof(Tier1Entity)).ShouldBeTrue();
	}

	[Fact]
	public void Entity_not_implementing_INorseEntity_throws_on_model_build()
	{
		var act = BuildModel<PlainContext>;

		var ex = act.ShouldThrow<InvalidOperationException>();
		ex.Message.ShouldContain(nameof(PlainEntity));
	}

	[Fact]
	public void Entity_implementing_INorseEntity_directly_satisfies_the_convention()
	{
		Should.NotThrow(BuildModel<DirectImplementationContext>);
	}

	static void BuildModel<TContext>() where TContext : DbContext
	{
		using var ctx = (TContext)Activator.CreateInstance(typeof(TContext),
			new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options)!;
		_ = ctx.Model;
	}

	sealed class Tier1Entity : NorseEntityBase<Tier1Entity>, INorseEntity<Tier1Entity>
	{
		public int Id { get; set; }

		public static void Configure(EntityTypeBuilder<Tier1Entity> builder) =>
			builder.Property(e => e.Id);
	}

	sealed class PlainEntity
	{
		public int Id { get; set; }
	}

	sealed class PlainContext(DbContextOptions<PlainContext> options) : NorseDbContext(options)
	{
		public DbSet<PlainEntity> Entities => Set<PlainEntity>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<PlainEntity>().Property(e => e.Id);
		}
	}

	sealed class DirectImplementationEntity : INorseEntity<DirectImplementationEntity>
	{
		public int Id { get; set; }

		public static void Configure(
			Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<DirectImplementationEntity> builder) =>
			builder.Property(e => e.Id);
	}

	sealed class DirectImplementationContext(DbContextOptions<DirectImplementationContext> options)
		: NorseDbContext(options)
	{
		public DbSet<DirectImplementationEntity> Entities => Set<DirectImplementationEntity>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<DirectImplementationEntity>().Property(e => e.Id);
		}
	}
}
