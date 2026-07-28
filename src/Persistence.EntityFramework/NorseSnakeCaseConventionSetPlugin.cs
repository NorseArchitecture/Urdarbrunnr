using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace Norse.Persistence.EntityFramework;

sealed class NorseSnakeCaseConventionSetPlugin(
	Func<string, string> rewriteName,
	Action<IConventionEntityType, Func<string, string>>? applyProviderSpecificRenames) : IConventionSetPlugin
{
	public ConventionSet ModifyConventions(ConventionSet conventionSet)
	{
		conventionSet.ModelFinalizingConventions.Add(new NorseSnakeCaseNamingConvention(rewriteName, applyProviderSpecificRenames));
		return conventionSet;
	}
}
