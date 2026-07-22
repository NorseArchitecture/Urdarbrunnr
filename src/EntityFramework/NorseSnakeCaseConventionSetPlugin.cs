using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Norse.EntityFramework;

sealed class NorseSnakeCaseConventionSetPlugin(
	Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames) : IConventionSetPlugin
{
	public ConventionSet ModifyConventions(ConventionSet conventionSet)
	{
		conventionSet.ModelFinalizingConventions.Add(new NorseSnakeCaseNamingConvention(applyProviderSpecificRenames));
		return conventionSet;
	}
}
