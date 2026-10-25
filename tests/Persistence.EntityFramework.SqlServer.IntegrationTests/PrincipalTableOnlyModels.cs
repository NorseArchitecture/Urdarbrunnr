using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework.SqlServer.IntegrationTests;

// The model pairs the live suites migrate between — the same shapes the snapshot suite
// (Persistence.EntityFramework.SqlServer.Tests/PrincipalTableOnlyMigrationSqlTests.cs) diffs, here with
// settable members so EF's real write path can be driven against the migrated schema. Every variant maps one
// table pair, Users split to UserLockout; each context type is its own cached model and overrides only the one
// thing its migration changes.

sealed class EmptyContext(DbContextOptions<EmptyContext> options) : NorseDbContext(options);

abstract class SplitContext<TUser>(DbContextOptions options) : NorseDbContext(options)
	where TUser : UserShape, INorseEntity<TUser>
{
	public DbSet<TUser> Users => Set<TUser>();

	protected virtual void Principal(EntityTypeBuilder<TUser> user) { }

	protected virtual void Fragment(SplitTableBuilder<TUser> lockout) { }

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		var user = builder.Entity<TUser>();
		TUser.Configure(user);
		Principal(user);
		user.ToTable(PrincipalTableOnlySchema.PrincipalTable)
			.SplitToTable(PrincipalTableOnlySchema.FragmentTable, lockout =>
			{
				lockout.Property(entity => entity.LockoutEnd);
				Fragment(lockout);
			});
	}
}

sealed class OptedInContext(DbContextOptions<OptedInContext> options) : SplitContext<PrincipalUser>(options);

sealed class PlainContext(DbContextOptions<PlainContext> options) : SplitContext<PlainUser>(options);

sealed class AddedColumnContext(DbContextOptions<AddedColumnContext> options) : SplitContext<PrincipalUser>(options)
{
	protected override void Principal(EntityTypeBuilder<PrincipalUser> user) =>
		user.Property<string>("Extra").HasMaxLength(32);
}

sealed class WidenedColumnContext(DbContextOptions<WidenedColumnContext> options)
	: SplitContext<PrincipalUser>(options)
{
	protected override void Principal(EntityTypeBuilder<PrincipalUser> user) =>
		user.Property(entity => entity.Name).HasMaxLength(200);
}

sealed class RequiredColumnContext(DbContextOptions<RequiredColumnContext> options)
	: SplitContext<PrincipalUser>(options)
{
	protected override void Principal(EntityTypeBuilder<PrincipalUser> user) =>
		user.Property<string>("Extra").HasMaxLength(32).IsRequired();
}

sealed class FragmentColumnContext(DbContextOptions<FragmentColumnContext> options)
	: SplitContext<PrincipalUser>(options)
{
	protected override void Principal(EntityTypeBuilder<PrincipalUser> user) =>
		user.Property<string>("Note").HasMaxLength(32);

	protected override void Fragment(SplitTableBuilder<PrincipalUser> lockout) =>
		lockout.Property<string>("Note");
}

abstract class UserShape
{
	public int Id { get; set; }

	[MaxLength(100)] public string Name { get; set; } = "";

	public DateTimeOffset? LockoutEnd { get; set; }
}

sealed class PrincipalUser : UserShape, ITemporalEntity, INorseEntity<PrincipalUser>
{
	public static void Configure(EntityTypeBuilder<PrincipalUser> builder) =>
		builder.TemporalOnPrincipalTableOnly();
}

sealed class PlainUser : UserShape, INorseEntity<PlainUser>
{
	public static void Configure(EntityTypeBuilder<PlainUser> builder) { }
}

/// <summary>The names every model above maps, and the ones the opt-in derives from them.</summary>
static class PrincipalTableOnlySchema
{
	public const string PrincipalTable = "Users";

	public const string FragmentTable = "UserLockout";

	public const string HistoryTable = "UsersHistory";

	public const string PeriodStart = "SystemPeriodStart";

	public const string PeriodEnd = "SystemPeriodEnd";
}
