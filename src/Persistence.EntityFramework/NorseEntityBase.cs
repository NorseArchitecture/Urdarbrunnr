namespace Norse.Persistence.EntityFramework;

/// <summary>
/// Base for Norse-owned entities with no competing base class (Tier 1). Brownfield entities that must
/// inherit a third-party base (<c>IdentityUser&lt;Guid&gt;</c>, etc.) implement
/// <see cref="INorseEntity{TSelf}"/> directly instead (Tier 2) — C# is single-inheritance and the slot
/// is already spent.
/// </summary>
/// <remarks>
/// A concrete <c>TSelf</c> must declare both this base and the interface explicitly —
/// <c>sealed class Foo : NorseEntityBase&lt;Foo&gt;, INorseEntity&lt;Foo&gt;</c>. The constraint here
/// requires <c>TSelf</c> to already satisfy <see cref="INorseEntity{TSelf}"/>; it does not grant the
/// interface to <c>TSelf</c> for free. Omitting the interface fails to build with <c>CS0311</c>;
/// omitting <c>Configure</c> fails with <c>CS0535</c>.
/// </remarks>
public abstract record NorseEntityBase<TSelf>
	where TSelf : NorseEntityBase<TSelf>, INorseEntity<TSelf>;
