using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Persistence.EntityFramework.Design;

/// <summary>
/// Installs <see cref="DdlEmittingMigrationsScaffolder"/> as EF's <see cref="IMigrationsScaffolder"/>.
/// A downstream realm's own <c>IDesignTimeServices</c> implementation calls this from its
/// <c>.Migrations.{Provider}</c> project -- the one place EF's tooling actually reflects over to
/// discover design-time services, so this boilerplate can't be hoisted any further up the chassis.
/// </summary>
/// <example>
/// <code>
/// sealed class DesignTimeServices : IDesignTimeServices
/// {
///     public void ConfigureDesignTimeServices(IServiceCollection services) =>
///         services.AddNorseDesignTimeServices("norse_referencedata");
/// }
/// </code>
/// </example>
public static class NorseDesignTimeServicesExtensions
{
	/// <param name="services">The design-time service collection EF's tooling supplies.</param>
	/// <param name="databaseName">
	/// The realm's database name (e.g. <c>"norse_referencedata"</c>) -- names the emitted schema
	/// file (<c>schema/{databaseName}.sql</c>, resolved via <see cref="DesignTimeSchemaPath"/>).
	/// </param>
	/// <returns>The same <paramref name="services"/> for chaining.</returns>
	public static IServiceCollection AddNorseDesignTimeServices(this IServiceCollection services, string databaseName)
	{
		var outputFilePath = DesignTimeSchemaPath.Resolve(AppContext.BaseDirectory, databaseName);
		var efDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IMigrationsScaffolder));

		return services.AddSingleton<IMigrationsScaffolder>(sp =>
		{
			var inner = efDescriptor switch
			{
				{ ImplementationType: not null } => (IMigrationsScaffolder)ActivatorUtilities.CreateInstance(sp, efDescriptor.ImplementationType),
				{ ImplementationFactory: not null } => (IMigrationsScaffolder)efDescriptor.ImplementationFactory(sp),
				{ ImplementationInstance: not null } => (IMigrationsScaffolder)efDescriptor.ImplementationInstance,
				_ => throw new InvalidOperationException(
					"Could not locate Entity Framework's IMigrationsScaffolder registration. Ensure Microsoft.EntityFrameworkCore.Design is referenced correctly.")
			};
			return new DdlEmittingMigrationsScaffolder(inner, sp.GetRequiredService<ICurrentDbContext>(), outputFilePath);
		});
	}
}
