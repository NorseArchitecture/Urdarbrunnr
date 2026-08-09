using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Persistence.EntityFramework;

/// <summary>
///     Carrier-only options extension delivering the provider binding's
///     <see cref="INorseEfProvider.TemporalRealizationHook" /> from
///     <see cref="NorseDbContextOptionsExtensions.ApplyNorseProviderOptions" /> to
///     <see cref="NorseDbContext.ConfigureConventions" />, where it rides
///     <see cref="TemporalEntityConvention" />'s stamp-then-realize pass. Registers no services and no
///     convention-set plugin deliberately: a plugin-added finalizing convention lands earlier in the
///     finalizing list than the context-added stamping convention and would run before the stamp exists.
/// </summary>
sealed class NorseTemporalRealizationOptionsExtension(Action<IConventionEntityType> temporalRealizationHook) :
	IDbContextOptionsExtension
{
	internal Action<IConventionEntityType> TemporalRealizationHook { get; } = temporalRealizationHook;

	public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

	public void ApplyServices(IServiceCollection services)
	{
	}

	public IDbContextOptionsExtension ApplyDefaults(IDbContextOptions options) =>
		this;

	public void Validate(IDbContextOptions options)
	{
	}

	sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
	{
		public override bool IsDatabaseProvider => false;

		public override string LogFragment => "using Norse temporal realization";

		// Applies no services, so any two instances may share an internal service provider.
		public override int GetServiceProviderHashCode() =>
			0;

		public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
			other is ExtensionInfo;

		public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
		{
		}
	}
}
