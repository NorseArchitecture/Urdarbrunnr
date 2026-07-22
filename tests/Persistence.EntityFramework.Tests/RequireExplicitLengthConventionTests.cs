using Microsoft.EntityFrameworkCore;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class RequireExplicitLengthConventionTests
{
	[Fact]
	void MaxLengthAttribute_carries_length()
	{
		MaxLengthAttribute attr = new(25);

		attr.Length.ShouldBe(25);
		attr.ShouldBeAssignableTo<MaxLengthAttribute>();
	}

	[Fact]
	void FixedLengthAttribute_carries_length()
	{
		FixedLengthAttribute attr = new(10);

		attr.Length.ShouldBe(10);
	}

	[Fact]
	void UnboundedLengthAttribute_carries_negative_one()
	{
		UnboundedLengthAttribute attr = new();

		attr.Length.ShouldBe(-1);
	}

	[Fact]
	void Unbounded_string_property_throws_on_model_build()
	{
		var act = BuildModel<UnboundedContext>;

		var ex = act.ShouldThrow<InvalidOperationException>();
		ex.Message.ShouldContain("UnboundedEntity.Value (String)");
	}

	[Fact]
	void MaxLength_attribute_satisfies_the_convention()
	{
		Should.NotThrow(BuildModel<AttributeBoundedContext>);
	}

	[Fact]
	void HasMaxLength_fluent_call_satisfies_the_convention()
	{
		Should.NotThrow(BuildModel<FluentBoundedContext>);
	}

	[Fact]
	void UnboundedLength_attribute_passes_as_explicit_negative_one()
	{
		Should.NotThrow(BuildModel<ExplicitUnboundedContext>);
	}

	[Fact]
	void FixedLength_attribute_does_not_set_IsFixedLength_on_non_SqlServer_providers()
	{
		using var ctx = CreateContext<FixedLengthContext>();

		var property = ctx.Model.FindEntityType(typeof(FixedLengthEntity))!
			.FindProperty(nameof(FixedLengthEntity.Value))!;

		property.GetMaxLength().ShouldBe(10);
		property.IsFixedLength().ShouldNotBe(true);
	}

	[Fact]
	void FixedLength_attribute_sets_IsFixedLength_on_SqlServer()
	{
		using var ctx = CreateSqlServerContext<FixedLengthContext>();

		var property = ctx.Model.FindEntityType(typeof(FixedLengthEntity))!
			.FindProperty(nameof(FixedLengthEntity.Value))!;

		property.GetMaxLength().ShouldBe(10);
		property.IsFixedLength().ShouldBe(true);
	}

	[Fact]
	void String_property_converted_to_non_string_storage_type_is_skipped()
	{
		Should.NotThrow(BuildModel<ConvertedContext>);
	}

	[Fact]
	void Json_owned_type_property_is_skipped()
	{
		Should.NotThrow(BuildModel<JsonOwnedContext>);
	}

	[Fact]
	void Collects_every_violation_before_throwing()
	{
		var act = BuildModel<MultiUnboundedContext>;

		var ex = act.ShouldThrow<InvalidOperationException>();
		ex.Message.ShouldContain("First (String)");
		ex.Message.ShouldContain("Second (String)");
	}

	static TContext CreateContext<TContext>() where TContext : DbContext =>
		(TContext)Activator.CreateInstance(typeof(TContext),
			new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options)!;

	static TContext CreateSqlServerContext<TContext>() where TContext : DbContext =>
		(TContext)Activator.CreateInstance(typeof(TContext),
			new DbContextOptionsBuilder<TContext>()
				.UseSqlServer("Server=localhost;Database=test;Trusted_Connection=True;TrustServerCertificate=True;")
				.Options)!;

	static void BuildModel<TContext>() where TContext : DbContext
	{
		using var ctx = CreateContext<TContext>();
		_ = ctx.Model;
	}

	sealed class UnboundedEntity
	{
		public int Id { get; set; }
		public string Value { get; set; } = "";
	}

	sealed class UnboundedContext(DbContextOptions<UnboundedContext> options) : NorseDbContext(options)
	{
		public DbSet<UnboundedEntity> Entities => Set<UnboundedEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention(false));
	}

	sealed class AttributeBoundedEntity
	{
		public int Id { get; set; }

		[MaxLength(25)]
		public string Value { get; set; } = "";
	}

	sealed class AttributeBoundedContext(DbContextOptions<AttributeBoundedContext> options) : NorseDbContext(options)
	{
		public DbSet<AttributeBoundedEntity> Entities => Set<AttributeBoundedEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention(false));
	}

	sealed class FluentBoundedEntity
	{
		public int Id { get; set; }
		public string Value { get; set; } = "";
	}

	sealed class FluentBoundedContext(DbContextOptions<FluentBoundedContext> options) : NorseDbContext(options)
	{
		public DbSet<FluentBoundedEntity> Entities => Set<FluentBoundedEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention(false));

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<FluentBoundedEntity>().Property(e => e.Value).HasMaxLength(50);
		}
	}

	sealed class ExplicitUnboundedEntity
	{
		public int Id { get; set; }

		[UnboundedLength]
		public string Value { get; set; } = "";
	}

	sealed class ExplicitUnboundedContext(DbContextOptions<ExplicitUnboundedContext> options) : NorseDbContext(options)
	{
		public DbSet<ExplicitUnboundedEntity> Entities => Set<ExplicitUnboundedEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention(false));
	}

	sealed class FixedLengthEntity
	{
		public int Id { get; set; }

		[FixedLength(10)]
		public string Value { get; set; } = "";
	}

	sealed class FixedLengthContext(DbContextOptions<FixedLengthContext> options) : NorseDbContext(options)
	{
		public DbSet<FixedLengthEntity> Entities => Set<FixedLengthEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
		{
			var applyFixedLength = Database.ProviderName == NorseDbContextOptionsExtensions.SqlServerProviderName;
			configurationBuilder.Conventions.Add(_ => new RequireExplicitLengthConvention(applyFixedLength));
		}
	}

	sealed class ConvertedEntity
	{
		public int Id { get; set; }
		public string Value { get; set; } = "";
	}

	sealed class ConvertedContext(DbContextOptions<ConvertedContext> options) : NorseDbContext(options)
	{
		public DbSet<ConvertedEntity> Entities => Set<ConvertedEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention(false));

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<ConvertedEntity>().Property(e => e.Value)
				.HasConversion(s => Guid.Parse(s), g => g.ToString());
		}
	}

	sealed class JsonOwnedEntity
	{
		public int Id { get; set; }
		public JsonOwnedDetail Detail { get; set; } = new();
	}

	sealed class JsonOwnedDetail
	{
		public string Value { get; set; } = "";
	}

	sealed class JsonOwnedContext(DbContextOptions<JsonOwnedContext> options) : NorseDbContext(options)
	{
		public DbSet<JsonOwnedEntity> Entities => Set<JsonOwnedEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention(false));

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<JsonOwnedEntity>().OwnsOne(e => e.Detail, o => o.ToJson());
		}
	}

	sealed class MultiUnboundedEntity
	{
		public int Id { get; set; }
		public string First { get; set; } = "";
		public string Second { get; set; } = "";
	}

	sealed class MultiUnboundedContext(DbContextOptions<MultiUnboundedContext> options) : NorseDbContext(options)
	{
		public DbSet<MultiUnboundedEntity> Entities => Set<MultiUnboundedEntity>();

		protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
			configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention(false));
	}
}
