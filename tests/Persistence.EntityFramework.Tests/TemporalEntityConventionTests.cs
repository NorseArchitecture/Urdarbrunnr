using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class TemporalEntityConventionTests
{
	[Fact]
	void Stamps_the_temporal_annotation_on_a_marked_entity()
	{
		using var context = TestContext.Create<TemporalWidget, TemporalWidgetConfiguration>();

		var entity = context.Model.FindEntityType(typeof(TemporalWidget))!;

		entity.FindAnnotation(NorseAnnotationNames.Temporal)!.Value.ShouldBe(true);
	}

	[Fact]
	void Leaves_unmarked_entities_unstamped()
	{
		using var context = TestContext.Create<PlainWidget, PlainWidgetConfiguration>();

		context.Model.FindEntityType(typeof(PlainWidget))!
			.FindAnnotation(NorseAnnotationNames.Temporal).ShouldBeNull();
	}

	[Fact]
	void Throws_at_model_finalize_when_a_marked_entity_has_no_primary_key()
	{
		var act = () => TestContext.Create<KeylessTemporal, KeylessTemporalConfiguration>().Model;

		act.ShouldThrow<InvalidOperationException>().Message.ShouldContain("primary key");
	}

	[Fact]
	void Throws_at_model_finalize_when_a_table_claims_a_derived_history_name()
	{
		var act = () => TestContext.Create<Clash, ClashConfiguration>().Model;

		act.ShouldThrow<InvalidOperationException>().Message.ShouldContain("clash_history");
	}

	[Fact]
	void Throws_at_model_finalize_when_a_split_fragment_claims_a_derived_history_name()
	{
		var act = () => TestContext.Create<SplitTemporal, SplitTemporalConfiguration>().Model;

		act.ShouldThrow<InvalidOperationException>().Message.ShouldContain("split_temporal_history");
	}

	[Fact]
	void Throws_at_model_finalize_when_a_marked_entity_maps_a_property_to_system_period()
	{
		var act = () => TestContext.Create<SystemPeriodWidget, SystemPeriodWidgetConfiguration>().Model;

		act.ShouldThrow<InvalidOperationException>().Message.ShouldContain(nameof(SystemPeriodWidget.Period));
	}

	[Fact]
	void Builds_fine_when_an_unmarked_entity_maps_a_property_to_system_period()
	{
		using var context = TestContext.Create<PlainSystemPeriodWidget, PlainSystemPeriodWidgetConfiguration>();

		context.Model.FindEntityType(typeof(PlainSystemPeriodWidget)).ShouldNotBeNull();
	}

	[Fact]
	void The_park_fluent_stamps_the_park_annotation()
	{
		using var context = TestContext.Create<TemporalWidget, ParkedTemporalWidgetConfiguration>();

		context.Model.FindEntityType(typeof(TemporalWidget))!
			.FindAnnotation(NorseAnnotationNames.TemporalParkedOnSqlServer)!.Value.ShouldBe(true);
	}

	interface ITestConfiguration<TEntity> where TEntity : class
	{
		static abstract void Configure(ModelBuilder builder);
	}

	static class TestContext
	{
		public static TemporalTestContext<TEntity, TConfiguration> Create<TEntity, TConfiguration>()
			where TEntity : class
			where TConfiguration : ITestConfiguration<TEntity>
		{
			var optionsBuilder = new DbContextOptionsBuilder<TemporalTestContext<TEntity, TConfiguration>>()
				.UseSqlite("Data Source=:memory:");
			optionsBuilder.ApplyNorseConventions(NorseNameRewriters.LowerSnakeCase);
			return new(optionsBuilder.Options);
		}
	}

	sealed class TemporalTestContext<TEntity, TConfiguration>(
		DbContextOptions<TemporalTestContext<TEntity, TConfiguration>> options) :
		NorseDbContext(options)
		where TEntity : class
		where TConfiguration : ITestConfiguration<TEntity>
	{
		public DbSet<TEntity> Entities => Set<TEntity>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			TConfiguration.Configure(builder);
		}
	}

	sealed record TemporalWidget : ITemporalEntity, INorseEntity<TemporalWidget>
	{
		public int Id { get; init; }

		[MaxLength(64)] public string Name { get; init; } = "";

		public static void Configure(EntityTypeBuilder<TemporalWidget> builder)
		{
		}
	}

	sealed record PlainWidget : INorseEntity<PlainWidget>
	{
		public int Id { get; init; }

		public static void Configure(EntityTypeBuilder<PlainWidget> builder)
		{
		}
	}

	sealed record SystemPeriodWidget : ITemporalEntity, INorseEntity<SystemPeriodWidget>
	{
		public int Id { get; init; }

		public int Period { get; init; }

		public static void Configure(EntityTypeBuilder<SystemPeriodWidget> builder)
		{
		}
	}

	sealed record PlainSystemPeriodWidget : INorseEntity<PlainSystemPeriodWidget>
	{
		public int Id { get; init; }

		public int Period { get; init; }

		public static void Configure(EntityTypeBuilder<PlainSystemPeriodWidget> builder)
		{
		}
	}

	sealed record KeylessTemporal : ITemporalEntity, INorseEntity<KeylessTemporal>
	{
		[MaxLength(100)] public string Name { get; init; } = "";

		public static void Configure(EntityTypeBuilder<KeylessTemporal> builder)
		{
		}
	}

	sealed record Clash : ITemporalEntity, INorseEntity<Clash>
	{
		public int Id { get; init; }

		public static void Configure(EntityTypeBuilder<Clash> builder)
		{
		}
	}

	sealed record ClashHistory : INorseEntity<ClashHistory>
	{
		public int Id { get; init; }

		public static void Configure(EntityTypeBuilder<ClashHistory> builder)
		{
		}
	}

	sealed record SplitTemporal : ITemporalEntity, INorseEntity<SplitTemporal>
	{
		public int Id { get; init; }

		[MaxLength(64)] public string Name { get; init; } = "";

		[MaxLength(64)] public string Detail { get; init; } = "";

		public static void Configure(EntityTypeBuilder<SplitTemporal> builder)
		{
		}
	}

	sealed class TemporalWidgetConfiguration : ITestConfiguration<TemporalWidget>
	{
		public static void Configure(ModelBuilder builder)
		{
		}
	}

	sealed class PlainWidgetConfiguration : ITestConfiguration<PlainWidget>
	{
		public static void Configure(ModelBuilder builder)
		{
		}
	}

	sealed class SystemPeriodWidgetConfiguration : ITestConfiguration<SystemPeriodWidget>
	{
		public static void Configure(ModelBuilder builder) =>
			builder.Entity<SystemPeriodWidget>().Property(e => e.Period).HasColumnName("system_period");
	}

	sealed class PlainSystemPeriodWidgetConfiguration : ITestConfiguration<PlainSystemPeriodWidget>
	{
		public static void Configure(ModelBuilder builder) =>
			builder.Entity<PlainSystemPeriodWidget>().Property(e => e.Period).HasColumnName("system_period");
	}

	sealed class KeylessTemporalConfiguration : ITestConfiguration<KeylessTemporal>
	{
		public static void Configure(ModelBuilder builder) => builder.Entity<KeylessTemporal>().HasNoKey();
	}

	sealed class ClashConfiguration : ITestConfiguration<Clash>
	{
		public static void Configure(ModelBuilder builder)
		{
			builder.Entity<Clash>().ToTable("clash");
			builder.Entity<ClashHistory>().ToTable("clash_history");
		}
	}

	sealed class SplitTemporalConfiguration : ITestConfiguration<SplitTemporal>
	{
		public static void Configure(ModelBuilder builder) =>
			builder.Entity<SplitTemporal>(b =>
			{
				b.ToTable("split_temporal");
				b.SplitToTable("split_temporal_history", t => t.Property(e => e.Detail));
			});
	}

	sealed class ParkedTemporalWidgetConfiguration : ITestConfiguration<TemporalWidget>
	{
		public static void Configure(ModelBuilder builder) =>
			builder.Entity<TemporalWidget>().TemporalParkedOnSqlServer();
	}
}
