namespace Norse.EntityFramework;

/// <summary>
/// Marker interface implemented by all Norse EF contexts. Allows
/// <c>EfMigrationContributor&lt;TContext&gt;</c> to constrain
/// <c>TContext : DbContext, INorseDbContext</c> without forcing a single base class —
/// auth contexts inherit <c>IdentityDbContext</c>, not <see cref="NorseDbContext"/>.
/// </summary>
public interface INorseDbContext;
