using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Norse.Persistence.EntityFramework.Design;

/// <summary>
/// The one provider-neutral <see cref="IDesignTimeDbContextFactory{TContext}"/> base for Norse
/// contexts, used only by <c>dotnet ef</c> tooling. Consumes the same
/// <c>ApplyNorseProviderOptions</c> choreography as the runtime and migration-host registrations —
/// one copy, so design-time output cannot drift from what the running container produces. The
/// connection string is always the binding's inert placeholder: <c>migrations add</c>/<c>remove</c>
/// build the model offline and never open a connection; running migrations against real
/// infrastructure is the migration host's job, never design tooling's. There is deliberately no
/// environment-variable escape hatch.
/// </summary>
/// <typeparam name="TContext">The Norse EF context this factory constructs at design time.</typeparam>
public abstract class NorseDesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext>
	where TContext : DbContext, INorseDbContext
{
	/// <summary>The provider binding — names the provider this factory's realm project targets.</summary>
	protected abstract INorseEfProvider ProviderBinding { get; }

	/// <summary>The realm's database name — e.g. <c>"norse_reference"</c>.</summary>
	protected abstract string DatabaseName { get; }

	/// <inheritdoc />
	public TContext CreateDbContext(string[] args)
	{
		DbContextOptionsBuilder<TContext> optionsBuilder = new();
		ConfigureOptions(optionsBuilder);
		return CreateContext(optionsBuilder.Options);
	}

	/// <summary>
	/// Applies the shared provider-options choreography with the binding's placeholder connection
	/// string and this factory's own assembly as the migrations assembly. Override to layer in
	/// additional configuration (e.g. an ASP.NET Core Identity-style context calling
	/// <c>UseApplicationServiceProvider</c> to control schema version); call
	/// <c>base.ConfigureOptions(builder)</c> first unless deliberately replacing the wiring.
	/// </summary>
	/// <param name="builder">The options builder to configure.</param>
	protected virtual void ConfigureOptions(DbContextOptionsBuilder<TContext> builder) =>
		builder.ApplyNorseProviderOptions(ProviderBinding,
			ProviderBinding.DesignTimePlaceholderConnectionString(DatabaseName),
			GetType().Assembly.GetName().Name);

	/// <summary>Constructs <typeparamref name="TContext"/> from the configured options.</summary>
	/// <param name="options">The configured options.</param>
	/// <returns>The context instance.</returns>
	protected abstract TContext CreateContext(DbContextOptions<TContext> options);
}
