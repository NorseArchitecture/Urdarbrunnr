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
	/// <param name="applyFixedLength">
	/// Whether <see cref="FixedLengthAttribute"/> should translate to <c>.IsFixedLength()</c>. Pass
	/// <see langword="true"/> only for providers where fixed-length storage has a real benefit (SQL
	/// Server); Postgres and everything else should pass <see langword="false"/> — see
	/// <see cref="FixedLengthAttribute"/>'s remarks for why. No default: every caller states its
	/// provider explicitly rather than silently inheriting a guess.
	/// </param>
	/// <returns>The same <paramref name="configurationBuilder"/>, for chaining.</returns>
	public static ModelConfigurationBuilder Apply(ModelConfigurationBuilder configurationBuilder, bool applyFixedLength)
	{
		configurationBuilder.Conventions.Add(_ => new RequireExplicitLengthConvention(applyFixedLength));
		configurationBuilder.Conventions.Add(static _ => new RequireEntityConfigurationConvention());
		return configurationBuilder;
	}
}
