using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Norse.Primitives.Identifiers;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class SequentialGuidConversionWiringTests
{
	[Fact]
	void Non_SqlServer_providers_get_the_Rfc9562_converter()
	{
		using var ctx = CreateContext<SequentialGuidContext>();

		var property = ctx.Model.FindEntityType(typeof(SequentialGuidEntity))!
			.FindProperty(nameof(SequentialGuidEntity.Id))!;

		property.GetValueConverter().ShouldBeOfType<Rfc9562SequentialGuidValueConverter>();
	}

	[Fact]
	void SqlServer_gets_the_SqlServer_converter()
	{
		using var ctx = CreateSqlServerContext<SequentialGuidContext>();

		var property = ctx.Model.FindEntityType(typeof(SequentialGuidEntity))!
			.FindProperty(nameof(SequentialGuidEntity.Id))!;

		property.GetValueConverter().ShouldBeOfType<SqlServerSequentialGuidValueConverter>();
	}

	static TContext CreateContext<TContext>() where TContext : DbContext =>
		(TContext)Activator.CreateInstance(typeof(TContext),
			new DbContextOptionsBuilder<TContext>().UseSqlite("Data Source=:memory:").Options)!;

	static TContext CreateSqlServerContext<TContext>() where TContext : DbContext =>
		(TContext)Activator.CreateInstance(typeof(TContext),
			new DbContextOptionsBuilder<TContext>()
				.UseSqlServer("Server=localhost;Database=test;Trusted_Connection=True;TrustServerCertificate=True;")
				.Options)!;

	sealed record SequentialGuidEntity(SequentialGuid Id) : NorseEntityBase<SequentialGuidEntity>, INorseEntity<SequentialGuidEntity>
	{
		public static void Configure(EntityTypeBuilder<SequentialGuidEntity> builder) =>
			builder.HasKey(e => e.Id);
	}

	sealed class SequentialGuidContext(DbContextOptions<SequentialGuidContext> options) : NorseDbContext(options)
	{
		public DbSet<SequentialGuidEntity> Entities => Set<SequentialGuidEntity>();
	}
}
