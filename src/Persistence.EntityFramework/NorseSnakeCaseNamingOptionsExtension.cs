using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Additive <see cref="IDbContextOptionsExtension"/> that registers
/// <see cref="NorseSnakeCaseConventionSetPlugin"/> via <see cref="IServiceCollection"/>'s <c>AddSingleton</c> —
/// deliberately not <c>ReplaceService</c>, which would silently clobber the DI registration slot if a
/// second <see cref="Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure.IConventionSetPlugin"/>
/// is ever added later. EF Core resolves that interface as
/// <see cref="IEnumerable{T}"/> when building the convention set, designed for exactly this kind of
/// additive composition.
/// </summary>
sealed class NorseSnakeCaseNamingOptionsExtension(
	Func<string, string> rewriteName,
	Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames) : IDbContextOptionsExtension
{
	// A primary-constructor-captured parameter is only resolvable as a bare identifier inside this
	// class's own instance members -- it is not a real member reachable via instance.parameterName,
	// even from a nested class after a cast. ExtensionInfo needs a genuine named member to read.
	internal Func<string, string> RewriteName { get; } = rewriteName;

	internal Action<IConventionEntityType, Func<string, string>>? ApplyProviderSpecificRenames { get; } = applyProviderSpecificRenames;

	public void ApplyServices(IServiceCollection services) =>
		services.AddSingleton<IConventionSetPlugin>(new NorseSnakeCaseConventionSetPlugin(RewriteName, ApplyProviderSpecificRenames));

	public IDbContextOptionsExtension ApplyDefaults(IDbContextOptions options) => this;

	public void Validate(IDbContextOptions options)
	{
	}

	public DbContextOptionsExtensionInfo Info =>
		new ExtensionInfo(this);

	sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
	{
		public override bool IsDatabaseProvider => false;

		public override string LogFragment => "using Norse snake_case naming";

		public override int GetServiceProviderHashCode() =>
			HashCode.Combine(
				((NorseSnakeCaseNamingOptionsExtension)Extension).RewriteName,
				((NorseSnakeCaseNamingOptionsExtension)Extension).ApplyProviderSpecificRenames);

		public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
			other is ExtensionInfo otherInfo &&
			Equals(
				((NorseSnakeCaseNamingOptionsExtension)Extension).RewriteName,
				((NorseSnakeCaseNamingOptionsExtension)otherInfo.Extension).RewriteName) &&
			Equals(
				((NorseSnakeCaseNamingOptionsExtension)Extension).ApplyProviderSpecificRenames,
				((NorseSnakeCaseNamingOptionsExtension)otherInfo.Extension).ApplyProviderSpecificRenames);

		public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
		{
		}
	}
}
