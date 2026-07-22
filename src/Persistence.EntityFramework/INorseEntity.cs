using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Every Norse entity is its own configuration. Implementing this interface obligates the concrete
/// type to supply <see cref="Configure"/> — the compiler refuses to build until it exists. Static
/// (not instance-based like EF Core's own <c>IEntityTypeConfiguration&lt;T&gt;</c>) so the generator
/// (<c>EntityConfigurationApplicationGenerator</c>, Norse.Persistence.EntityFramework.Generator)
/// never constructs an instance purely to call this method.
/// </summary>
public interface INorseEntity<TSelf> where TSelf : class, INorseEntity<TSelf>
{
	/// <summary>
	/// Configures the entity type using the provided <see cref="EntityTypeBuilder{TEntity}"/>.
	/// Every concrete implementation of <see cref="INorseEntity{TSelf}"/> must supply this method —
	/// static interface members are not inherited via virtual dispatch, so the compiler enforces
	/// that every concrete <typeparamref name="TSelf"/> provides its own.
	/// </summary>
	/// <param name="builder">The builder used to configure the entity type.</param>
	static abstract void Configure(EntityTypeBuilder<TSelf> builder);
}
