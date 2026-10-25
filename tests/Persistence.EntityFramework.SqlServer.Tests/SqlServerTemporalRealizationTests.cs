using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework.SqlServer.Tests;

public sealed class SqlServerTemporalRealizationTests
{
	[Fact]
	void A_marked_unsplit_entity_becomes_native_temporal_with_the_norse_period_names()
	{
		using var context = SqlServerTestContext.Create<TemporalOrder>();
		// Period configuration is not stored in the runtime read-optimized model.
		var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TemporalOrder))!;

		entity.IsTemporal().ShouldBeTrue();
		entity.GetPeriodStartPropertyName().ShouldBe("SystemPeriodStart");
		entity.GetPeriodEndPropertyName().ShouldBe("SystemPeriodEnd");
		entity.GetHistoryTableName().ShouldBe("TemporalOrderHistory");
	}

	[Fact]
	void The_realized_model_carries_the_period_columns_as_shadow_properties()
	{
		using var context = SqlServerTestContext.Create<TemporalOrder>();
		// Period configuration is not stored in the runtime read-optimized model.
		var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TemporalOrder))!;

		entity.FindProperty("SystemPeriodStart").ShouldNotBeNull();
		entity.FindProperty("SystemPeriodEnd").ShouldNotBeNull();
	}

	[Fact]
	void The_scaffolded_ddl_carries_native_system_versioning_against_the_history_table()
	{
		using var context = SqlServerTestContext.Create<TemporalOrder>();

		var script = context.Database.GenerateCreateScript();

		script.ShouldContain("SYSTEM_VERSIONING = ON");
		script.ShouldContain("TemporalOrderHistory");
	}

	[Fact]
	void Split_temporal_entity_without_opt_in_throws_naming_TemporalOnPrincipalTableOnly()
	{
		var act = () => SqlServerTestContext.Create<SplitTemporalUser>().Model;

		act.ShouldThrow<InvalidOperationException>().Message.ShouldContain("TemporalOnPrincipalTableOnly()");
	}

	[Fact]
	void Opt_in_on_an_unsplit_entity_throws()
	{
		var act = () => SqlServerTestContext.Create<OptedInUnsplitTemporalUser>().Model;

		var message = act.ShouldThrow<InvalidOperationException>().Message;
		message.ShouldContain("TemporalOnPrincipalTableOnly() applies only to entity-split temporal entities");
		message.ShouldContain(nameof(OptedInUnsplitTemporalUser));
	}

	[Fact]
	void Opted_in_split_entity_is_not_EF_temporal_on_SQL_Server()
	{
		using var context = SqlServerTestContext.Create<OptedInSplitTemporalUser>();
		var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(OptedInSplitTemporalUser))!;

		entity.IsTemporal().ShouldBeFalse();
		// HIDDEN periods live in DDL only (PoC G2): nothing in the model names them.
		entity.FindProperty("SystemPeriodStart").ShouldBeNull();
		entity.FindProperty("SystemPeriodEnd").ShouldBeNull();
	}

	[Fact]
	void Opted_in_split_entity_stamps_the_principal_table_mapping_only()
	{
		using var context = SqlServerTestContext.Create<OptedInSplitTemporalUser>();
		var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(OptedInSplitTemporalUser))!;

		entity.FindAnnotation(NorseAnnotationNames.Temporal)!.Value.ShouldBe(true);
		entity.FindAnnotation(NorseAnnotationNames.TemporalPrincipalTableOnly)!.Value.ShouldBe(true);
		var fragment = entity.GetMappingFragments(StoreObjectType.Table).ShouldHaveSingleItem();
		fragment.StoreObject.Name.ShouldBe("user_lockout");
		fragment.FindAnnotation(NorseAnnotationNames.Temporal).ShouldBeNull();
	}

	[Fact]
	void Non_split_temporal_entity_remains_EF_native()
	{
		// One model holding both shapes: the opt-in is read per entity, never model-wide.
		using var context = new MixedTemporalContext(SqlServerTestContext.Options<MixedTemporalContext>());
		var model = context.GetService<IDesignTimeModel>().Model;
		var order = model.FindEntityType(typeof(TemporalOrder))!;

		order.IsTemporal().ShouldBeTrue();
		order.GetPeriodStartPropertyName().ShouldBe("SystemPeriodStart");
		order.GetPeriodEndPropertyName().ShouldBe("SystemPeriodEnd");
		order.GetHistoryTableName().ShouldBe("TemporalOrderHistory");
		model.FindEntityType(typeof(OptedInSplitTemporalUser))!.IsTemporal().ShouldBeFalse();
	}

	static void ConfigureSplitTemporalUser(EntityTypeBuilder<SplitTemporalUser> builder) =>
		builder.SplitToTable("user_lockout", static lockout => lockout.Property(user => user.LockoutEnd));

	static void ConfigureOptedInSplitTemporalUser(EntityTypeBuilder<OptedInSplitTemporalUser> builder)
	{
		OptedInSplitTemporalUser.Configure(builder);
		builder.SplitToTable("user_lockout", static lockout => lockout.Property(user => user.LockoutEnd));
	}

	static class SqlServerTestContext
	{
		public static DbContextOptions<TContext> Options<TContext>() where TContext : DbContext
		{
			DbContextOptionsBuilder<TContext> optionsBuilder = new();
			optionsBuilder.ApplyNorseProviderOptions(NorseSqlServerEfProvider.Instance,
				"Server=localhost;Database=test;Trusted_Connection=True;TrustServerCertificate=True;",
				migrationsAssemblyName: null);
			return optionsBuilder.Options;
		}

		public static TemporalTestContext<TEntity> Create<TEntity>() where TEntity : class =>
			new(Options<TemporalTestContext<TEntity>>());
	}

	sealed class MixedTemporalContext(DbContextOptions<MixedTemporalContext> options) : NorseDbContext(options)
	{
		public DbSet<TemporalOrder> Orders => Set<TemporalOrder>();

		public DbSet<OptedInSplitTemporalUser> Users => Set<OptedInSplitTemporalUser>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<TemporalOrder>().ToTable("TemporalOrder");
			ConfigureOptedInSplitTemporalUser(builder.Entity<OptedInSplitTemporalUser>());
		}
	}

	sealed class TemporalTestContext<TEntity>(DbContextOptions<TemporalTestContext<TEntity>> options)
		: NorseDbContext(options)
		where TEntity : class
	{
		public DbSet<TEntity> Entities => Set<TEntity>();

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			if (typeof(TEntity) == typeof(TemporalOrder))
				builder.Entity<TemporalOrder>().ToTable("TemporalOrder");
			else if (typeof(TEntity) == typeof(SplitTemporalUser))
				ConfigureSplitTemporalUser(builder.Entity<SplitTemporalUser>());
			else if (typeof(TEntity) == typeof(OptedInSplitTemporalUser))
				ConfigureOptedInSplitTemporalUser(builder.Entity<OptedInSplitTemporalUser>());
			else if (typeof(TEntity) == typeof(OptedInUnsplitTemporalUser))
				OptedInUnsplitTemporalUser.Configure(builder.Entity<OptedInUnsplitTemporalUser>());
		}
	}

	sealed record TemporalOrder : ITemporalEntity, INorseEntity<TemporalOrder>
	{
		public int Id { get; init; }

		[MaxLength(100)] public string Description { get; init; } = "";

		public static void Configure(EntityTypeBuilder<TemporalOrder> builder)
		{
		}
	}

	sealed record SplitTemporalUser : ITemporalEntity, INorseEntity<SplitTemporalUser>
	{
		public int Id { get; init; }
		[MaxLength(100)] public string Name { get; init; } = "";
		public DateTimeOffset? LockoutEnd { get; init; }

		public static void Configure(EntityTypeBuilder<SplitTemporalUser> builder)
		{
		}
	}

	sealed record OptedInSplitTemporalUser : ITemporalEntity, INorseEntity<OptedInSplitTemporalUser>
	{
		public int Id { get; init; }
		[MaxLength(100)] public string Name { get; init; } = "";
		public DateTimeOffset? LockoutEnd { get; init; }

		public static void Configure(EntityTypeBuilder<OptedInSplitTemporalUser> builder) =>
			builder.TemporalOnPrincipalTableOnly();
	}

	sealed record OptedInUnsplitTemporalUser : ITemporalEntity, INorseEntity<OptedInUnsplitTemporalUser>
	{
		public int Id { get; init; }
		[MaxLength(100)] public string Name { get; init; } = "";

		public static void Configure(EntityTypeBuilder<OptedInUnsplitTemporalUser> builder) =>
			builder.TemporalOnPrincipalTableOnly();
	}
}
