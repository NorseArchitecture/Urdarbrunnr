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
	void A_marked_split_entity_without_the_park_declaration_throws_at_model_finalize()
	{
		var act = () => SqlServerTestContext.Create<SplitTemporalUser>().Model;

		act.ShouldThrow<InvalidOperationException>().Message.ShouldContain("TemporalParkedOnSqlServer");
	}

	[Fact]
	void A_parked_split_entity_skips_temporality_on_sql_server_only()
	{
		using var context = SqlServerTestContext.Create<ParkedSplitTemporalUser>();

		context.Model.FindEntityType(typeof(ParkedSplitTemporalUser))!.IsTemporal().ShouldBeFalse();
	}

	static class SqlServerTestContext
	{
		public static TemporalTestContext<TEntity> Create<TEntity>() where TEntity : class
		{
			var optionsBuilder = new DbContextOptionsBuilder<TemporalTestContext<TEntity>>();
			optionsBuilder.ApplyNorseProviderOptions(NorseSqlServerEfProvider.Instance,
				"Server=localhost;Database=test;Trusted_Connection=True;TrustServerCertificate=True;",
				migrationsAssemblyName: null);
			return new(optionsBuilder.Options);
		}
	}

	sealed class TemporalTestContext<TEntity>(DbContextOptions<TemporalTestContext<TEntity>> options) : NorseDbContext(options)
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
			else if (typeof(TEntity) == typeof(ParkedSplitTemporalUser))
				ConfigureParkedSplitTemporalUser(builder.Entity<ParkedSplitTemporalUser>());
		}
	}

	sealed record TemporalOrder : ITemporalEntity, INorseEntity<TemporalOrder>
	{
		public int Id { get; init; }

		[MaxLength(100)]
		public string Description { get; init; } = "";

		public static void Configure(EntityTypeBuilder<TemporalOrder> builder) { }
	}

	sealed record SplitTemporalUser : ITemporalEntity, INorseEntity<SplitTemporalUser>
	{
		public int Id { get; init; }
		[MaxLength(100)] public string Name { get; init; } = "";
		public DateTimeOffset? LockoutEnd { get; init; }

		public static void Configure(EntityTypeBuilder<SplitTemporalUser> builder) { }
	}

	sealed record ParkedSplitTemporalUser : ITemporalEntity, INorseEntity<ParkedSplitTemporalUser>
	{
		public int Id { get; init; }
		[MaxLength(100)] public string Name { get; init; } = "";
		public DateTimeOffset? LockoutEnd { get; init; }

		public static void Configure(EntityTypeBuilder<ParkedSplitTemporalUser> builder) =>
			builder.TemporalParkedOnSqlServer();
	}

	static void ConfigureSplitTemporalUser(EntityTypeBuilder<SplitTemporalUser> builder) =>
		builder.SplitToTable("user_lockout", static lockout => lockout.Property(user => user.LockoutEnd));

	static void ConfigureParkedSplitTemporalUser(EntityTypeBuilder<ParkedSplitTemporalUser> builder)
	{
		ParkedSplitTemporalUser.Configure(builder);
		builder.SplitToTable("user_lockout", static lockout => lockout.Property(user => user.LockoutEnd));
	}
}
