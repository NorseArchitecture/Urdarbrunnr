using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Norse.Abstractions.Emit;

namespace Norse.Persistence.EntityFramework.Migrations.Generator;

#pragma warning disable RS2008 // No analyzer-release ledger, matching the platform's other generators.

/// <summary>
/// Discovers every <c>EfMigrationContributor&lt;TContext&gt;</c> and <c>ISeedContributor</c> visible
/// to a migrations service's compilation, along with the single <c>INorseEfMigrationProvider</c>
/// binding it references, derives each context's migrations assembly from its <c>ModelSnapshot</c>,
/// and emits the provider-neutral <c>AddNorseMigrations()</c> choreography.
/// </summary>
[Generator]
public sealed class MigrationContributorGenerator : IIncrementalGenerator
{
	static readonly DiagnosticDescriptor _noProvider = new(
		"NORSE030", "No provider binding referenced",
		"Migration contributors were found but no INorseEfMigrationProvider implementation is visible to this compilation — reference exactly one provider binding package, for example Norse.Persistence.EntityFramework.PostgreSQL", "Norse.Migrations",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	static readonly DiagnosticDescriptor _multipleProviders = new(
		"NORSE031", "Multiple provider bindings referenced",
		"Exactly one INorseEfMigrationProvider implementation must be visible to a migrations compilation; found: {0}", "Norse.Migrations",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	static readonly DiagnosticDescriptor _noSnapshot = new(
		"NORSE032", "No ModelSnapshot for context",
		"No ModelSnapshot annotated with [DbContext(typeof({0}))] is visible to this compilation — the migrations assembly cannot be derived; reference the realm's *.Migrations.{{Provider}} project", "Norse.Migrations",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	static readonly DiagnosticDescriptor _ambiguousSnapshot = new(
		"NORSE034", "Multiple ModelSnapshots for context",
		"More than one assembly visible to this compilation carries a ModelSnapshot annotated with [DbContext(typeof({0}))] — the migrations assembly is ambiguous and reference order must never decide it; reference exactly one provider's *.Migrations.{{Provider}} project; candidates: {1}", "Norse.Migrations",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	static readonly DiagnosticDescriptor _noInstance = new(
		"NORSE033", "Provider binding missing Instance",
		"Provider binding '{0}' must expose a public static Instance property — the generated AddNorseMigrations() consumes the binding through it", "Norse.Migrations",
		DiagnosticSeverity.Error, isEnabledByDefault: true);

	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var model = context.CompilationProvider.Select(static (compilation, _) => (
			Contributors: MigrationContributorDiscovery.FindContributors(compilation),
			SeedContributors: MigrationContributorDiscovery.FindSeedContributors(compilation),
			Providers: MigrationContributorDiscovery.FindMigrationProviders(compilation)));

		context.RegisterSourceOutput(model, static (ctx, model) =>
		{
			if (model.Contributors.Count == 0 && model.SeedContributors.Count == 0)
				return;

			// Null exactly when this compilation has no migration contributors — the seed-only case,
			// which needs no binding at all. The type carries the invariant so BuildSource cannot
			// silently emit `.Instance` off a default-constructed ProviderInfo.
			ProviderInfo? provider = null;
			if (model.Contributors.Count > 0)
			{
				if (model.Providers.Count == 0)
				{
					ctx.ReportDiagnostic(Diagnostic.Create(_noProvider, Location.None));
					return;
				}

				if (model.Providers.Count > 1)
				{
					ctx.ReportDiagnostic(Diagnostic.Create(_multipleProviders, Location.None,
						string.Join(", ", model.Providers.Select(p => p.TypeDisplayName))));
					return;
				}

				var binding = model.Providers[0];
				if (!binding.HasInstance)
				{
					ctx.ReportDiagnostic(Diagnostic.Create(_noInstance, Location.None,
						binding.TypeDisplayName));
					return;
				}

				provider = binding;

				var missingSnapshots = model.Contributors
					.Where(c => c.MigrationsAssemblyNames.Count == 0).ToList();
				var ambiguousSnapshots = model.Contributors
					.Where(c => c.MigrationsAssemblyNames.Count > 1).ToList();
				if (missingSnapshots.Count > 0 || ambiguousSnapshots.Count > 0)
				{
					foreach (var c in missingSnapshots)
						ctx.ReportDiagnostic(Diagnostic.Create(_noSnapshot, Location.None,
							c.ContextType));

					foreach (var c in ambiguousSnapshots)
						ctx.ReportDiagnostic(Diagnostic.Create(_ambiguousSnapshot, Location.None,
							c.ContextType, string.Join(", ", c.MigrationsAssemblyNames)));

					return;
				}
			}

			var source = BuildSource(model.Contributors, model.SeedContributors, provider);
			ctx.AddSource("NorseMigrationsExtensions.g.cs", SourceText.From(source, Utf8NoBom.Encoding));
		});
	}

	static string BuildSource(IList<ContributorInfo> contributors,
		IList<SeedContributorInfo> seedContributors, ProviderInfo? provider)
	{
		// The binding is null exactly in the seed-only case, which emits no contributor registrations
		// and therefore never names it. Resolved once, here, so the invariant lives in one place —
		// and throws loudly if it ever stops holding, rather than emitting `.Instance` off nothing.
		string? providerTypeName = null;
		if (contributors.Count > 0)
			providerTypeName = (provider ?? throw new InvalidOperationException(
				"Migration contributors reached emission without a resolved provider binding.")).TypeDisplayName;

		StringBuilder sb = new();

		sb.AppendCSharp(
			"""
			// <auto-generated />
			#nullable enable
			using Microsoft.EntityFrameworkCore;
			using Microsoft.Extensions.DependencyInjection;
			using Microsoft.Extensions.Hosting;
			using Norse.Abstractions.Migrations;
			using Norse.Abstractions.Migrations.Seeding;
			using Norse.Persistence.EntityFramework.Migrations;
			using Norse.Infrastructure.Migrations;

			static class NorseMigrationsGeneratedExtensions
			{
				public static global::Microsoft.Extensions.Hosting.IHostApplicationBuilder AddNorseMigrations(
					this global::Microsoft.Extensions.Hosting.IHostApplicationBuilder builder)
				{
			""");

		foreach (var c in contributors)
			sb.AppendCSharp(
				$"""
						builder.AddNorseMigrationContext<{c.ContextType}>({providerTypeName}.Instance, "{c.ConnectionStringName}", "{c.MigrationsAssemblyNames[0]}");
						builder.Services.AddTransient<global::Norse.Abstractions.Migrations.IMigrationContributor, {c.ContributorType}>();
				""");

		foreach (var s in seedContributors)
			sb.AppendCSharp(
				$"""
						ConfigureSeedContributor<{s.ContributorType}>(builder.Services);
						builder.Services.AddTransient<global::Norse.Abstractions.Migrations.Seeding.ISeedContributor, {s.ContributorType}>();
				""");

		sb.AppendCSharp(
			"""
					builder.AddNorseMigrationsRunner();
					builder.AddNorseSeedingRunner();
					return builder;
				}
			""");

		if (seedContributors.Count > 0)
			sb.AppendCSharp(
				"""
					static void ConfigureSeedContributor<T>(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
						where T : global::Norse.Abstractions.Migrations.Seeding.ISeedContributor
						=> T.ConfigureServices(services);
				""");

		sb.AppendCSharp(
			"}");

		return sb.ToString();
	}
}
