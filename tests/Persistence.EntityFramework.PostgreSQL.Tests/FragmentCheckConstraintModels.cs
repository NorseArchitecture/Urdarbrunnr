using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

/// <summary>
///     The names and SQL the fragment check-constraint suites share. The entity is split the way Himinbjörg's users
///     are: the principal keeps the name, the credentials fragment holds the two lookup hashes the constraint
///     relates — the columns live only in the fragment, which is the whole reason EF's own check constraint cannot
///     be used.
/// </summary>
static class FragmentChecks
{
	public const string PrincipalTable = "accounts";

	public const string FragmentTable = "account_credentials";

	public const string Constraint = "ck_account_credentials_email_is_user_name";

	public const string Sql = "normalized_email = normalized_user_name";

	public const string ReworkedSql = "normalized_email = normalized_user_name AND normalized_email <> ''";

	public const string AnnotationName = $"{NorseAnnotationNames.FragmentCheckConstraintPrefix}{Constraint}";
}

sealed class NoAccountsContext(DbContextOptions<NoAccountsContext> options) : NorseDbContext(options);

// Every variant maps the same table pair, Accounts split to AccountCredentials; each context type is its own
// cached model and overrides only the one thing its migration changes.
abstract class AccountsContext<TAccount>(DbContextOptions options) : NorseDbContext(options)
	where TAccount : AccountShape, INorseEntity<TAccount>
{
	public DbSet<TAccount> Accounts => Set<TAccount>();

	protected virtual void Credentials(SplitTableBuilder<TAccount> credentials) { }

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		var account = builder.Entity<TAccount>();
		TAccount.Configure(account);
		account
			.ToTable("Accounts")
			.SplitToTable("AccountCredentials", credentials =>
			{
				credentials.Property(entity => entity.NormalizedEmail);
				credentials.Property(entity => entity.NormalizedUserName);
				Credentials(credentials);
			});
	}
}

sealed class UncheckedAccountsContext(DbContextOptions<UncheckedAccountsContext> options)
	: AccountsContext<Account>(options);

sealed class CheckedAccountsContext(DbContextOptions<CheckedAccountsContext> options)
	: AccountsContext<Account>(options)
{
	protected override void Credentials(SplitTableBuilder<Account> credentials) =>
		credentials.HasFragmentCheckConstraint(FragmentChecks.Constraint, FragmentChecks.Sql);
}

sealed class RecheckedAccountsContext(DbContextOptions<RecheckedAccountsContext> options)
	: AccountsContext<Account>(options)
{
	protected override void Credentials(SplitTableBuilder<Account> credentials) =>
		credentials.HasFragmentCheckConstraint(FragmentChecks.Constraint, FragmentChecks.ReworkedSql);
}

// The Himinbjörg shape: the principal is temporal, the fragment carrying the constraint never is.
sealed class TemporalCheckedAccountsContext(DbContextOptions<TemporalCheckedAccountsContext> options)
	: AccountsContext<TemporalAccount>(options)
{
	protected override void Credentials(SplitTableBuilder<TemporalAccount> credentials) =>
		credentials.HasFragmentCheckConstraint(FragmentChecks.Constraint, FragmentChecks.Sql);
}

abstract record AccountShape
{
	public int Id { get; init; }

	[MaxLength(100)] public string Name { get; init; } = "";

	[MaxLength(256)] public string NormalizedEmail { get; init; } = "";

	[MaxLength(256)] public string NormalizedUserName { get; init; } = "";
}

sealed record Account : AccountShape, INorseEntity<Account>
{
	public static void Configure(EntityTypeBuilder<Account> builder) { }
}

sealed record TemporalAccount : AccountShape, ITemporalEntity, INorseEntity<TemporalAccount>
{
	public static void Configure(EntityTypeBuilder<TemporalAccount> builder) { }
}
