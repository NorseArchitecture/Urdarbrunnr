using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Norse.EntityFramework.PostgreSQL.Tests;

public sealed class NorsePostgresContextExtensionsTests
{
	[Fact]
	public void AddNorsePostgresContext_registers_TContext_in_DI()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = "Host=localhost;Database=test" });

		builder.AddNorsePostgresContext<TestContext>("test-db");

		var descriptor = builder.Services
			.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options);
}
