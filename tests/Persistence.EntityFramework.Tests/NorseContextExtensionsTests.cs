using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.Tests;

public sealed class NorseContextExtensionsTests
{
	static HostApplicationBuilder CreateBuilder(string? connectionString = "Data Source=:memory:")
	{
		var builder = Host.CreateApplicationBuilder();
		if (connectionString is not null)
			builder.Configuration.AddInMemoryCollection(
				new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = connectionString });
		return builder;
	}

	[Fact]
	void AddNorseContext_registers_TContext_pooled()
	{
		var builder = CreateBuilder();

		builder.AddNorseContext<TestContext>(new FakeEfProvider(), "test-db");

		var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
		// AddDbContextPool registers TContext via a pool-leasing factory (ImplementationFactory set,
		// ImplementationType null) — the inverse of the non-pooled shape asserted in the
		// migration-context tests.
		descriptor.ImplementationFactory.ShouldNotBeNull();
		descriptor.ImplementationType.ShouldBeNull();
	}

	[Fact]
	void AddNorseContext_resolves_the_connection_string_and_passes_no_migrations_assembly()
	{
		var builder = CreateBuilder();
		FakeEfProvider provider = new();

		builder.AddNorseContext<TestContext>(provider, "test-db");
		using var host = builder.Build();
		_ = host.Services.GetRequiredService<DbContextOptions<TestContext>>();

		provider.SeenConnectionString.ShouldBe("Data Source=:memory:");
		provider.MigrationsAssemblySeen.ShouldBeTrue();
		provider.SeenMigrationsAssemblyName.ShouldBeNull();
	}

	[Fact]
	void AddNorseContext_throws_loudly_when_the_connection_string_is_missing()
	{
		var builder = CreateBuilder(connectionString: null);

		var ex = Should.Throw<InvalidOperationException>(() =>
			builder.AddNorseContext<TestContext>(new FakeEfProvider(), "test-db"));
		ex.Message.ShouldContain("test-db");
	}

	[Fact]
	void AddNorseContext_applies_the_no_tracking_law_unconditionally()
	{
		var builder = CreateBuilder();

		builder.AddNorseContext<TestContext>(new FakeEfProvider(), "test-db");
		using var host = builder.Build();
		var options = host.Services.GetRequiredService<DbContextOptions<TestContext>>();

		options.GetExtension<CoreOptionsExtension>()
			.QueryTrackingBehavior.ShouldBe(QueryTrackingBehavior.NoTracking);
	}

	[Fact]
	void AddNorseContext_applies_naming_only_when_the_binding_supplies_a_rewriter()
	{
		var withRewriter = CreateBuilder();
		withRewriter.AddNorseContext<TestContext>(
			new FakeEfProvider { NameRewriter = NorseNameRewriters.LowerSnakeCase }, "test-db");
		using var host1 = withRewriter.Build();
		host1.Services.GetRequiredService<DbContextOptions<TestContext>>()
			.FindExtension<NorseSnakeCaseNamingOptionsExtension>().ShouldNotBeNull();

		var withoutRewriter = CreateBuilder();
		withoutRewriter.AddNorseContext<TestContext>(new FakeEfProvider(), "test-db");
		using var host2 = withoutRewriter.Build();
		host2.Services.GetRequiredService<DbContextOptions<TestContext>>()
			.FindExtension<NorseSnakeCaseNamingOptionsExtension>().ShouldBeNull();
	}

	[Fact]
	void AddNorseContext_enriches_through_the_binding()
	{
		var builder = CreateBuilder();
		FakeEfProvider provider = new();

		builder.AddNorseContext<TestContext>(provider, "test-db");

		provider.EnrichCalls.ShouldBe(1);
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options);
}
