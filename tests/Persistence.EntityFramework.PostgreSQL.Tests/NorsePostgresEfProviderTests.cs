using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norse.Persistence.EntityFramework.Migrations;
using Npgsql;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

public sealed class NorsePostgresEfProviderTests
{
	const string ConnectionString = "Host=localhost;Database=test";

	static HostApplicationBuilder CreateBuilder()
	{
		var builder = Host.CreateApplicationBuilder();
		builder.Configuration.AddInMemoryCollection(
			new Dictionary<string, string?> { ["ConnectionStrings:test-db"] = ConnectionString });
		return builder;
	}

	[Fact]
	void AddNorseContext_covers_every_service_Aspires_AddNpgsqlDbContext_registers()
	{
		// THE ASPIRE-EQUIVALENCE GATE (spec §5): the design drops Aspire's Add{P}DbContext sugar on
		// the claim that AddDbContextPool + Enrich{P}DbContext is its documented equivalent. This
		// test holds that claim to account. If it fails, DO NOT loosen the assertion silently:
		// investigate each missing ServiceType; a difference may only be excluded here with a
		// written justification comment naming the type and why it is not load-bearing. If a
		// difference IS load-bearing (pooling or instrumentation cannot be reconstructed), HALT
		// the plan and surface it — that is a design-level finding, not an implementation detail.
		// Scope note: this compares registered ServiceTypes only, NOT Aspire's connection-string
		// precedence semantics — the AppHost live run in the adoption sweep owns that question,
		// and a green gate here does not close it.
		var aspire = CreateBuilder();
		aspire.AddNpgsqlDbContext<TestContext>("test-db");

		var norse = CreateBuilder();
		norse.AddNorseContext<TestContext>(NorsePostgresEfProvider.Instance, "test-db");

		var aspireTypes = aspire.Services.Select(d => d.ServiceType).ToHashSet();
		var norseTypes = norse.Services.Select(d => d.ServiceType).ToHashSet();
		aspireTypes.Except(norseTypes).ShouldBeEmpty();
	}

	[Fact]
	void AddNorseContext_registers_TContext_pooled()
	{
		var builder = CreateBuilder();

		builder.AddNorseContext<TestContext>(NorsePostgresEfProvider.Instance, "test-db");

		var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
		descriptor.ImplementationFactory.ShouldNotBeNull();
	}

	[Fact]
	void AddNorseMigrationContext_registers_TContext_non_pooled_and_does_not_throw_building_the_model()
	{
		var builder = CreateBuilder();

		builder.AddNorseMigrationContext<TestContext>(NorsePostgresEfProvider.Instance, "test-db",
			"Norse.Persistence.EntityFramework.PostgreSQL.Tests");

		var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(TestContext));
		descriptor.ShouldNotBeNull();
		descriptor.ImplementationType.ShouldBe(typeof(TestContext));
		descriptor.ImplementationFactory.ShouldBeNull();

		using var host = builder.Build();
		using var scope = host.Services.CreateScope();
		using var ctx = scope.ServiceProvider.GetRequiredService<TestContext>();
		Should.NotThrow(() => _ = ctx.Model);
	}

	[Fact]
	void Binding_supplies_lower_snake_naming_as_postgres_engine_native_style()
	{
		NorsePostgresEfProvider.Instance.NameRewriter.ShouldNotBeNull();
		NorsePostgresEfProvider.Instance.NameRewriter("CountryOrArea").ShouldBe("country_or_area");
		NorsePostgresEfProvider.Instance.EntityRenameHook.ShouldBeNull();
	}

	[Fact]
	void Design_time_placeholder_parses_but_points_at_nothing()
	{
		var placeholder = NorsePostgresEfProvider.Instance
			.DesignTimePlaceholderConnectionString("norse_reference");

		// IDE0028 false positive: NpgsqlConnectionStringBuilder implements IDictionary, and the
		// collection-expression heuristic (dotnet_style_prefer_collection_expression =
		// when_types_loosely_match) misfires on its single-string constructor overload, suggesting
		// `[placeholder]` — not a valid replacement for parsing a connection string.
#pragma warning disable IDE0028
		NpgsqlConnectionStringBuilder parsed = new(placeholder);
#pragma warning restore IDE0028
		parsed.Database.ShouldBe("norse_reference");
		parsed.Host.ShouldBe("design");
	}

	sealed class TestContext(DbContextOptions<TestContext> options) : NorseDbContext(options);
}
