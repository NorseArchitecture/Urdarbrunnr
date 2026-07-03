using Microsoft.EntityFrameworkCore;

namespace Norse.EntityFramework;

/// <summary>
/// Registers the model-finalizing conventions every Norse EF context is guaranteed to enforce:
/// explicit string/byte[] length (<see cref="RequireExplicitLengthConvention"/>) and mandatory
/// entity self-configuration (<see cref="RequireEntityConfigurationConvention"/>).
/// </summary>
public static class NorseModelConventions
{
	/// <summary>
	/// Adds both Norse model-finalizing conventions to <paramref name="configurationBuilder"/>.
	/// </summary>
	/// <param name="configurationBuilder">The configuration builder to register conventions on.</param>
	/// <returns>The same <paramref name="configurationBuilder"/>, for chaining.</returns>
	public static ModelConfigurationBuilder Apply(ModelConfigurationBuilder configurationBuilder)
	{
		configurationBuilder.Conventions.Add(static _ => new RequireExplicitLengthConvention());
		configurationBuilder.Conventions.Add(static _ => new RequireEntityConfigurationConvention());
		return configurationBuilder;
	}
}
