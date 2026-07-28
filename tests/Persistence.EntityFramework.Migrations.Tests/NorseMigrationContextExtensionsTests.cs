using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Norse.Persistence.EntityFramework.Migrations.Tests;

public sealed class NorseMigrationContextExtensionsTests
{
	static HostApplicationBuilder CreateBuilder()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = "Data Source=:memory:" });
		return builder;
	}

	[Fact]
	void AddNorseMigrationContext_registers_TContext_non_pooled()
	{
		var builder = CreateBuilder();

		builder.AddNorseMigrationContext<TestContext>(new FakeEfProvider(), "test-db",
			"Test.Migrations.Assembly");

		var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
		// AddDbContext registers TContext type-to-type (ImplementationType set, no factory) — the
		// inverse of the pooled shape; a migrations service constructs its context once and exits,
		// and EF forbids OnConfiguring mutating frozen pooled options.
		descriptor.ImplementationType.ShouldBe(typeof(TestContext));
		descriptor.ImplementationFactory.ShouldBeNull();
	}

	[Fact]
	void AddNorseMigrationContext_forwards_the_migrations_assembly_to_the_binding()
	{
		var builder = CreateBuilder();
		FakeEfProvider provider = new();

		builder.AddNorseMigrationContext<TestContext>(provider, "test-db", "Test.Migrations.Assembly");
		using var host = builder.Build();
		_ = host.Services.GetRequiredService<DbContextOptions<TestContext>>();

		provider.SeenConnectionString.ShouldBe("Data Source=:memory:");
		provider.SeenMigrationsAssemblyName.ShouldBe("Test.Migrations.Assembly");
		provider.EnrichCalls.ShouldBe(1);
	}

	[Fact]
	void AddNorseMigrationContext_throws_loudly_when_the_connection_string_is_missing()
	{
		var builder = Host.CreateApplicationBuilder();

		var ex = Should.Throw<InvalidOperationException>(() =>
			builder.AddNorseMigrationContext<TestContext>(new FakeEfProvider(), "absent-db", "X"));
		ex.Message.ShouldContain("absent-db");
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options);
}
