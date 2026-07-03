namespace Norse.EntityFramework;

/// <summary>
/// Base for Norse-owned entities with no competing base class (Tier 1). Brownfield entities that must
/// inherit a third-party base (<c>IdentityUser&lt;Guid&gt;</c>, etc.) implement
/// <see cref="INorseEntity{TSelf}"/> directly instead (Tier 2) — C# is single-inheritance and the slot
/// is already spent.
/// </summary>
public abstract class NorseEntityBase<TSelf>
	where TSelf : NorseEntityBase<TSelf>, INorseEntity<TSelf>;
