using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Norse.Persistence.EntityFramework.SqlServer.Tests;

/// <summary>
///     DDL coverage for <c>TemporalOnPrincipalTableOnly()</c> on SQL Server (spec §6.1/§6.2). No database and
///     no hand-built operations: EF's real model differ produces the operations from two built models, and the
///     real generator — as <see cref="NorseSqlServerEfProvider" /> wires it — turns them into SQL, so the
///     annotation projection, the differ and the emission are all under test in one arrange.
/// </summary>
public sealed class PrincipalTableOnlyMigrationSqlTests
{
	const string PrincipalTable = "Users";

	const string FragmentTable = "UserLockout";

	const string PeriodStartDefault = "DF_Users_SystemPeriodStart";

	const string PeriodEndDefault = "DF_Users_SystemPeriodEnd";

	[Fact]
	void Diff_from_empty_to_opted_in_model_emits_hidden_periods_and_versioning()
	{
		using EmptyContext from = new(Options<EmptyContext>());
		using OptedInContext to = new(Options<OptedInContext>());

		var create = CommandCreating(DiffSql(from, to), PrincipalTable);

		create.ShouldContain("[SystemPeriodStart] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL");
		create.ShouldContain("[SystemPeriodEnd] datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL");
		create.ShouldContain("PERIOD FOR SYSTEM_TIME ([SystemPeriodStart], [SystemPeriodEnd])");
		create.ShouldContain("CONSTRAINT [PK_Users] PRIMARY KEY ([Id])");
		create.ShouldContain("WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[UsersHistory]))");
		// Inline (spec §6.1): one statement, the versioning clause trailing the column list.
		create.IndexOf("PERIOD FOR SYSTEM_TIME", StringComparison.Ordinal)
			.ShouldBeLessThan(create.IndexOf("WITH (SYSTEM_VERSIONING", StringComparison.Ordinal));
		create.ShouldNotContain("ALTER TABLE");
	}

	[Fact]
	void Diff_emits_no_temporal_DDL_for_the_fragment_table()
	{
		using EmptyContext from = new(Options<EmptyContext>());
		using OptedInContext to = new(Options<OptedInContext>());

		var commands = DiffSql(from, to);
		var fragment = CommandCreating(commands, FragmentTable);

		fragment.ShouldNotContain("SYSTEM_VERSIONING");
		fragment.ShouldNotContain("PERIOD FOR");
		fragment.ShouldNotContain("SystemPeriod");
		fragment.ShouldNotContain("HIDDEN");
		Occurrences(string.Join(Environment.NewLine, commands), "SYSTEM_VERSIONING = ON").ShouldBe(1);
	}

	[Fact]
	void Diff_from_opted_in_model_to_empty_disables_versioning_then_drops_history_then_table()
	{
		using OptedInContext from = new(Options<OptedInContext>());
		using EmptyContext to = new(Options<EmptyContext>());

		var commands = DiffSql(from, to);
		// One batch (G5): the whole teardown is a single command, so no GO splits it in a script.
		var drop = commands
			.Where(command => command.Contains("DROP TABLE [Users];", StringComparison.Ordinal))
			.ShouldHaveSingleItem();

		var off = drop.IndexOf("ALTER TABLE [Users] SET (SYSTEM_VERSIONING = OFF);", StringComparison.Ordinal);
		var history = drop.IndexOf("DROP TABLE [dbo].[UsersHistory];", StringComparison.Ordinal);
		var table = drop.IndexOf("DROP TABLE [Users];", StringComparison.Ordinal);
		off.ShouldBeGreaterThanOrEqualTo(0);
		off.ShouldBeLessThan(history);
		history.ShouldBeLessThan(table);
		commands.Single(command => command.Contains($"DROP TABLE [{FragmentTable}]", StringComparison.Ordinal))
			.ShouldNotContain("SYSTEM_VERSIONING");
	}

	[Fact]
	void Adding_a_principal_column_is_emitted_with_versioning_left_on()
	{
		using OptedInContext from = new(Options<OptedInContext>());
		using AddedColumnContext to = new(Options<AddedColumnContext>());

		// The engine propagates the column to history itself (G5), so the add is the whole migration.
		DiffSql(from, to).ShouldHaveSingleItem()
			.ShouldBe($"ALTER TABLE [Users] ADD [Extra] nvarchar(32) NULL;{Environment.NewLine}");
	}

	[Fact]
	void Dropping_a_principal_column_is_emitted_with_versioning_left_on()
	{
		using AddedColumnContext from = new(Options<AddedColumnContext>());
		using OptedInContext to = new(Options<OptedInContext>());

		var drop = DiffSql(from, to).ShouldHaveSingleItem();

		drop.ShouldContain("ALTER TABLE [Users] DROP COLUMN [Extra];");
		ShouldNotWrapVersioning(drop);
	}

	[Fact]
	void Altering_a_principal_column_is_emitted_with_versioning_left_on()
	{
		using OptedInContext from = new(Options<OptedInContext>());
		using WidenedColumnContext to = new(Options<WidenedColumnContext>());

		// ALTER COLUMN propagates to history on a HIDDEN-period table too (G5): no statement names history.
		var alter = DiffSql(from, to).ShouldHaveSingleItem();

		alter.ShouldContain("ALTER TABLE [Users] ALTER COLUMN [Name] nvarchar(200) NOT NULL;");
		ShouldNotWrapVersioning(alter);
		alter.ShouldNotContain("UsersHistory");
	}

	[Fact]
	void Fragment_column_changes_emit_plain_DDL()
	{
		using OptedInContext from = new(Options<OptedInContext>());
		using FragmentColumnContext to = new(Options<FragmentColumnContext>());

		var add = DiffSql(from, to).ShouldHaveSingleItem();

		add.ShouldContain("ALTER TABLE [UserLockout] ADD [Note] nvarchar(32) NULL;");
		ShouldNotWrapVersioning(add);
		add.ShouldNotContain("SystemPeriod");
	}

	[Fact]
	void Adding_the_opt_in_to_an_existing_model_emits_enable()
	{
		var enable = EnableSql();

		// The defaults are named so the disable path can drop them, and are the only enable mechanism on a
		// populated table: GENERATED ALWAYS columns cannot be backfilled by UPDATE (4901, G5).
		var positions = PositionsOf(enable,
		[
			"ALTER TABLE [Users] ADD",
			$"[SystemPeriodStart] datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL CONSTRAINT [{PeriodStartDefault}] DEFAULT SYSUTCDATETIME(),",
			$"[SystemPeriodEnd] datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL CONSTRAINT [{PeriodEndDefault}] DEFAULT CONVERT(datetime2, '9999-12-31 23:59:59.9999999'),",
			"PERIOD FOR SYSTEM_TIME ([SystemPeriodStart], [SystemPeriodEnd]);",
			"ALTER TABLE [Users] SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[UsersHistory]));"
		]);
		positions.ShouldBe([.. positions.Order()], "add the periods, then turn versioning on");
		enable.ShouldNotContain(FragmentTable);
		enable.ShouldNotContain("SYSTEM_VERSIONING = OFF");
	}

	[Fact]
	void Removing_the_opt_in_emits_disable()
	{
		var disable = DisableSql();

		// After DROP PERIOD the formerly hidden columns are visible, so the teardown is one unit (G5).
		var positions = PositionsOf(disable,
		[
			"ALTER TABLE [Users] SET (SYSTEM_VERSIONING = OFF);",
			"ALTER TABLE [Users] DROP PERIOD FOR SYSTEM_TIME;",
			$"ALTER TABLE [Users] DROP CONSTRAINT IF EXISTS [{PeriodStartDefault}];",
			$"ALTER TABLE [Users] DROP CONSTRAINT IF EXISTS [{PeriodEndDefault}];",
			"ALTER TABLE [Users] DROP COLUMN [SystemPeriodStart], [SystemPeriodEnd];",
			"DROP TABLE [dbo].[UsersHistory];"
		]);
		positions.ShouldBe([.. positions.Order()], "the teardown order is the only appliable one");
		disable.ShouldNotContain(FragmentTable);
		disable.ShouldNotContain("SYSTEM_VERSIONING = ON");
	}

	[Fact]
	void Disable_after_enable_drops_the_period_defaults()
	{
		// A table that was enabled rather than created carries the enable step's default constraints, and
		// DROP COLUMN fails on them (5074, G5) unless the disable drops exactly the names the enable created.
		var enable = EnableSql();
		var disable = DisableSql();

		foreach (var constraint in (string[])[PeriodStartDefault, PeriodEndDefault])
		{
			enable.ShouldContain($"CONSTRAINT [{constraint}] DEFAULT");
			disable.IndexOf($"DROP CONSTRAINT IF EXISTS [{constraint}];", StringComparison.Ordinal)
				.ShouldBeInRange(0, disable.IndexOf("DROP COLUMN [SystemPeriodStart]", StringComparison.Ordinal) - 1);
		}
	}

	[Fact]
	void A_primary_key_change_on_the_principal_is_rejected_by_name()
	{
		var message = RejectionOf<OptedInContext, ReKeyedContext>();

		message.ShouldContain("Users");
		message.ShouldContain("primary key", Case.Insensitive);
		message.ShouldContain("TemporalOnPrincipalTableOnly()");
	}

	[Fact]
	void A_principal_table_rename_is_rejected_by_name()
	{
		var message = RejectionOf<OptedInContext, RenamedContext>();

		message.ShouldContain("Renaming");
		message.ShouldContain("'Users'");
		message.ShouldContain("UsersHistory");
	}

	[Fact]
	void A_principal_schema_move_is_rejected_by_name()
	{
		var message = RejectionOf<OptedInContext, SchemaMovedContext>();

		message.ShouldContain("schema move to 'audit'", Case.Insensitive);
		message.ShouldContain("'Users'");
	}

	[Fact]
	void A_computed_column_added_to_the_principal_is_rejected_naming_the_manual_sequence()
	{
		var message = RejectionOf<OptedInContext, ComputedColumnContext>();

		message.ShouldContain("computed column 'NameLength'");
		message.ShouldContain("13724");
		// OFF in a batch of its own → add → plain history column → ON with the consistency check (G5/G6).
		PositionsOf(message,
		[
			"ALTER TABLE [Users] SET (SYSTEM_VERSIONING = OFF);",
			"in a batch of its own",
			"[dbo].[UsersHistory]",
			"ALTER TABLE [Users] SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[UsersHistory], DATA_CONSISTENCY_CHECK = ON));"
		]).ShouldBeInOrder();
	}

	[Fact]
	void An_identity_column_added_to_the_principal_is_rejected_naming_the_manual_sequence()
	{
		var message = RejectionOf<UngeneratedKeyContext, IdentityColumnContext>();

		message.ShouldContain("identity column 'Sequence'");
		message.ShouldContain("13704");
		message.ShouldContain("DATA_CONSISTENCY_CHECK = ON");
	}

	[Fact]
	void A_nullable_principal_column_made_required_is_rejected_naming_the_manual_sequence()
	{
		var message = RejectionOf<AddedColumnContext, RequiredColumnContext>();

		message.ShouldContain("column 'Extra'");
		message.ShouldContain("'Users'");
		message.ShouldContain("TemporalOnPrincipalTableOnly()");
		message.ShouldContain("Msg 515");
		// History is the audit record: the author decides what its NULLs become; the chassis never backfills it.
		PositionsOf(message,
		[
			"ALTER TABLE [Users] SET (SYSTEM_VERSIONING = OFF);",
			"in a batch of its own",
			"backfill the NULLs of 'Extra' in [Users]",
			"decide deliberately what the NULLs recorded in [dbo].[UsersHistory] become",
			"ALTER TABLE [Users] ALTER COLUMN [Extra] nvarchar(32) NOT NULL;",
			"ALTER TABLE [dbo].[UsersHistory] ALTER COLUMN [Extra] nvarchar(32) NOT NULL;",
			"ALTER TABLE [Users] SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[UsersHistory], DATA_CONSISTENCY_CHECK = ON));"
		]).ShouldBeInOrder();
	}

	[Fact]
	void A_required_principal_column_made_nullable_is_emitted_with_versioning_left_on()
	{
		using RequiredColumnContext from = new(Options<RequiredColumnContext>());
		using AddedColumnContext to = new(Options<AddedColumnContext>());

		// Relaxing a constraint cannot be violated by any recorded version, so the engine's propagation is enough.
		var alter = DiffSql(from, to).ShouldHaveSingleItem();

		alter.ShouldContain("ALTER TABLE [Users] ALTER COLUMN [Extra] nvarchar(32) NULL;");
		ShouldNotWrapVersioning(alter);
		alter.ShouldNotContain("UsersHistory");
	}

	[Fact]
	void A_nullable_fragment_column_made_required_emits_plain_DDL()
	{
		using FragmentColumnContext from = new(Options<FragmentColumnContext>());
		using RequiredFragmentColumnContext to = new(Options<RequiredFragmentColumnContext>());

		// Fragments are never versioned, so there is no history for the constraint to reach.
		var alter = string.Join(Environment.NewLine, DiffSql(from, to));

		alter.ShouldContain("ALTER TABLE [UserLockout] ALTER COLUMN [Note] nvarchar(32) NOT NULL;");
		ShouldNotWrapVersioning(alter);
		alter.ShouldNotContain("UsersHistory");
	}

	[Fact]
	void Splitting_an_EF_native_temporal_entity_in_one_migration_is_rejected_by_name()
	{
		var message = RejectionOf<NativeTemporalContext, OptedInContext>();

		message.ShouldContain("'Users'");
		message.ShouldContain("EF-native");
		message.ShouldContain("TemporalOnPrincipalTableOnly()");
	}

	[Fact]
	void Unsplitting_an_opted_in_entity_in_one_migration_is_rejected_by_name()
	{
		var message = RejectionOf<OptedInContext, NativeTemporalContext>();

		message.ShouldContain("'Users'");
		message.ShouldContain("EF-native");
		message.ShouldContain("TemporalOnPrincipalTableOnly()");
	}

	static string EnableSql()
	{
		using PlainContext from = new(Options<PlainContext>());
		using OptedInContext to = new(Options<OptedInContext>());

		// One batch (G5): every enable statement lands in the single command.
		return DiffSql(from, to).ShouldHaveSingleItem();
	}

	static string DisableSql()
	{
		using OptedInContext from = new(Options<OptedInContext>());
		using PlainContext to = new(Options<PlainContext>());

		return DiffSql(from, to).ShouldHaveSingleItem();
	}

	static string RejectionOf<TFrom, TTo>()
		where TFrom : DbContext
		where TTo : DbContext
	{
		using var from = (DbContext)Activator.CreateInstance(typeof(TFrom), Options<TFrom>())!;
		using var to = (DbContext)Activator.CreateInstance(typeof(TTo), Options<TTo>())!;

		return Should.Throw<InvalidOperationException>(() => DiffSql(from, to)).Message;
	}

	static void ShouldNotWrapVersioning(string sql)
	{
		sql.ShouldNotContain("SYSTEM_VERSIONING");
		sql.ShouldNotContain("PERIOD FOR");
	}

	static List<int> PositionsOf(string sql, string[] statements) =>
	[
		.. statements.Select(statement =>
		{
			var position = sql.IndexOf(statement, StringComparison.Ordinal);
			position.ShouldBeGreaterThanOrEqualTo(0, $"'{statement}' should have been emitted");
			return position;
		})
	];

	static string CommandCreating(IReadOnlyList<string> commands, string table) =>
		commands
			.Where(command => command.Contains($"CREATE TABLE [{table}] (", StringComparison.Ordinal))
			.ShouldHaveSingleItem();

	static IReadOnlyList<string> DiffSql(DbContext from, DbContext to)
	{
		var targetModel = to.GetService<IDesignTimeModel>().Model;
		var operations = to.GetService<IMigrationsModelDiffer>().GetDifferences(
			from.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
			targetModel.GetRelationalModel());
		return
		[
			.. to.GetService<IMigrationsSqlGenerator>().Generate(operations, targetModel)
				.Select(command => command.CommandText)
		];
	}

	static DbContextOptions<TContext> Options<TContext>() where TContext : DbContext
	{
		DbContextOptionsBuilder<TContext> optionsBuilder = new();
		optionsBuilder.ApplyNorseProviderOptions(NorseSqlServerEfProvider.Instance,
			NorseSqlServerEfProvider.Instance.DesignTimePlaceholderConnectionString("norse_test"),
			migrationsAssemblyName: null);
		return optionsBuilder.Options;
	}

	static int Occurrences(string sql, string value) =>
		sql.Split(value).Length - 1;

	sealed class EmptyContext(DbContextOptions<EmptyContext> options) : NorseDbContext(options);

	// Every variant maps one table pair, Users split to UserLockout; each context type is its own cached model,
	// and each overrides only the one thing its migration changes.
	abstract class SplitContext<TUser>(DbContextOptions options) : NorseDbContext(options)
		where TUser : UserShape, INorseEntity<TUser>
	{
		protected virtual string Table => PrincipalTable;

		protected virtual string? Schema => null;

		protected virtual void Principal(EntityTypeBuilder<TUser> user) { }

		protected virtual void Fragment(SplitTableBuilder<TUser> lockout) { }

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			var user = builder.Entity<TUser>();
			TUser.Configure(user);
			Principal(user);
			user.ToTable(Table, Schema)
				.SplitToTable(FragmentTable, lockout =>
				{
					lockout.Property(entity => entity.LockoutEnd);
					Fragment(lockout);
				});
		}
	}

	sealed class OptedInContext(DbContextOptions<OptedInContext> options) : SplitContext<PrincipalUser>(options);

	sealed class PlainContext(DbContextOptions<PlainContext> options) : SplitContext<PlainUser>(options);

	sealed class AddedColumnContext(DbContextOptions<AddedColumnContext> options)
		: SplitContext<PrincipalUser>(options)
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

	sealed class FragmentColumnContext(DbContextOptions<FragmentColumnContext> options)
		: SplitContext<PrincipalUser>(options)
	{
		protected override void Principal(EntityTypeBuilder<PrincipalUser> user) =>
			user.Property<string>("Note").HasMaxLength(32);

		protected override void Fragment(SplitTableBuilder<PrincipalUser> lockout) =>
			lockout.Property<string>("Note");
	}

	sealed class RequiredColumnContext(DbContextOptions<RequiredColumnContext> options)
		: SplitContext<PrincipalUser>(options)
	{
		protected override void Principal(EntityTypeBuilder<PrincipalUser> user) =>
			user.Property<string>("Extra").HasMaxLength(32).IsRequired();
	}

	sealed class RequiredFragmentColumnContext(DbContextOptions<RequiredFragmentColumnContext> options)
		: SplitContext<PrincipalUser>(options)
	{
		protected override void Principal(EntityTypeBuilder<PrincipalUser> user) =>
			user.Property<string>("Note").HasMaxLength(32).IsRequired();

		protected override void Fragment(SplitTableBuilder<PrincipalUser> lockout) =>
			lockout.Property<string>("Note");
	}

	sealed class ReKeyedContext(DbContextOptions<ReKeyedContext> options) : SplitContext<PrincipalUser>(options)
	{
		protected override void Principal(EntityTypeBuilder<PrincipalUser> user)
		{
			user.Property<Guid>("Uid");
			user.HasKey("Uid");
		}
	}

	sealed class RenamedContext(DbContextOptions<RenamedContext> options) : SplitContext<PrincipalUser>(options)
	{
		protected override string Table => "Members";
	}

	sealed class SchemaMovedContext(DbContextOptions<SchemaMovedContext> options)
		: SplitContext<PrincipalUser>(options)
	{
		protected override string? Schema => "audit";
	}

	sealed class ComputedColumnContext(DbContextOptions<ComputedColumnContext> options)
		: SplitContext<PrincipalUser>(options)
	{
		protected override void Principal(EntityTypeBuilder<PrincipalUser> user) =>
			user.Property<int?>("NameLength").HasComputedColumnSql("LEN([Name])");
	}

	// SQL Server allows one identity column per table, so the identity pair starts from a key that is not one.
	sealed class UngeneratedKeyContext(DbContextOptions<UngeneratedKeyContext> options)
		: SplitContext<PrincipalUser>(options)
	{
		protected override void Principal(EntityTypeBuilder<PrincipalUser> user) =>
			user.Property(entity => entity.Id).ValueGeneratedNever();
	}

	sealed class IdentityColumnContext(DbContextOptions<IdentityColumnContext> options)
		: SplitContext<PrincipalUser>(options)
	{
		protected override void Principal(EntityTypeBuilder<PrincipalUser> user)
		{
			user.Property(entity => entity.Id).ValueGeneratedNever();
			user.Property<int>("Sequence").UseIdentityColumn();
		}
	}

	// The same entity unsplit: EF-native temporal, the realization every non-split entity gets.
	sealed class NativeTemporalContext(DbContextOptions<NativeTemporalContext> options) : NorseDbContext(options)
	{
		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			var user = builder.Entity<NativeTemporalUser>();
			NativeTemporalUser.Configure(user);
			user.ToTable(PrincipalTable);
		}
	}

	abstract record UserShape
	{
		public int Id { get; init; }
		[MaxLength(100)] public string Name { get; init; } = "";
		public DateTimeOffset? LockoutEnd { get; init; }
	}

	sealed record PrincipalUser : UserShape, ITemporalEntity, INorseEntity<PrincipalUser>
	{
		public static void Configure(EntityTypeBuilder<PrincipalUser> builder) =>
			builder.TemporalOnPrincipalTableOnly();
	}

	sealed record PlainUser : UserShape, INorseEntity<PlainUser>
	{
		public static void Configure(EntityTypeBuilder<PlainUser> builder) { }
	}

	sealed record NativeTemporalUser : UserShape, ITemporalEntity, INorseEntity<NativeTemporalUser>
	{
		public static void Configure(EntityTypeBuilder<NativeTemporalUser> builder) { }
	}
}
