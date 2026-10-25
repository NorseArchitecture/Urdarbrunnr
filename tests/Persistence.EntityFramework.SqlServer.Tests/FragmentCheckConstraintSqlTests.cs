using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.SqlServer.Design.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Norse.Persistence.EntityFramework.SqlServer.Tests;

// EF1001: SQL Server's design-time services and EF's design-time registration are EF-internal by attribute; they
// are what `dotnet ef migrations add` runs, which is the path the scaffold and snapshot facts measure.
#pragma warning disable EF1001

/// <summary>
///     <c>HasFragmentCheckConstraint</c> on SQL Server, through EF's real model differ and the generator exactly as
///     <see cref="NorseSqlServerEfProvider" /> wires it: no hand-built operation and no database. The constraint has
///     to land on the fragment's table — EF's own entity-level check constraint lands on the principal's, where
///     the columns it names do not exist — including the fragment of a <c>TemporalOnPrincipalTableOnly()</c>
///     entity, which is never versioned, so its constraint is plain DDL with no versioning wrapper.
/// </summary>
public sealed class FragmentCheckConstraintSqlTests
{
	const string Provider = "Microsoft.EntityFrameworkCore.SqlServer";

	const string PrincipalTable = "Accounts";

	const string FragmentTable = "AccountCredentials";

	const string Constraint = "CK_AccountCredentials_EmailIsUserName";

	const string Sql = "[NormalizedEmail] = [NormalizedUserName]";

	const string ReworkedSql = "[NormalizedEmail] = [NormalizedUserName] AND [NormalizedEmail] <> N''";

	const string AnnotationName = $"{NorseAnnotationNames.FragmentCheckConstraintPrefix}{Constraint}";

	const string Add = $"ALTER TABLE [{FragmentTable}] ADD CONSTRAINT [{Constraint}] CHECK ({Sql});";

	const string Drop = $"ALTER TABLE [{FragmentTable}] DROP CONSTRAINT [{Constraint}];";

	[Fact]
	void Fragment_check_constraint_is_emitted_on_the_fragment_table_not_the_principal()
	{
		var commands = DiffSql<NoAccountsContext, CheckedAccountsContext>();

		Creating(commands, PrincipalTable).ShouldNotContain("CHECK");
		var check = commands.Where(command => command.Contains("CHECK", StringComparison.Ordinal)).ShouldHaveSingleItem();
		check.ShouldBe($"{Add}{Environment.NewLine}");
		commands.IndexOf(Creating(commands, FragmentTable)).ShouldBeLessThan(commands.IndexOf(check));
	}

	[Fact]
	void An_entity_level_check_constraint_is_what_EF_would_have_put_on_the_principal()
	{
		// The gap this seam closes, kept standing: the CHECK EF emits for the same expression declared the only
		// way it offers lands inside the principal's CREATE TABLE, where neither column exists.
		var commands = DiffSql<NoAccountsContext, EntityLevelCheckedAccountsContext>();

		Creating(commands, PrincipalTable).ShouldContain($"CONSTRAINT [{Constraint}] CHECK ({Sql})");
		Creating(commands, FragmentTable).ShouldNotContain("CHECK");
	}

	[Fact]
	void Adding_a_fragment_check_constraint_emits_ADD_CONSTRAINT_on_the_fragment()
	{
		DiffSql<UncheckedAccountsContext, CheckedAccountsContext>().ShouldHaveSingleItem()
			.ShouldBe($"{Add}{Environment.NewLine}");
	}

	[Fact]
	void Removing_a_fragment_check_constraint_emits_DROP_CONSTRAINT()
	{
		DiffSql<CheckedAccountsContext, UncheckedAccountsContext>().ShouldHaveSingleItem()
			.ShouldBe($"{Drop}{Environment.NewLine}");
	}

	[Fact]
	void Changing_a_fragment_check_constraints_SQL_drops_it_then_adds_it_back()
	{
		DiffSql<CheckedAccountsContext, RecheckedAccountsContext>().ShouldBe(
		[
			$"{Drop}{Environment.NewLine}",
			$"ALTER TABLE [{FragmentTable}] ADD CONSTRAINT [{Constraint}] CHECK ({ReworkedSql});{Environment.NewLine}"
		]);
	}

	[Fact]
	void An_unchanged_fragment_check_constraint_emits_nothing()
	{
		DiffSql<CheckedAccountsContext, CheckedAccountsContext>().ShouldBeEmpty();
	}

	[Fact]
	void The_fragment_of_a_principal_table_only_entity_takes_the_constraint_as_plain_DDL()
	{
		// The fragment is never versioned (spec §6.1), so its CHECK carries no versioning wrapper, and the
		// versioned principal's CREATE TABLE carries no CHECK.
		var commands = DiffSql<NoAccountsContext, PrincipalTableOnlyCheckedAccountsContext>();

		var principal = Creating(commands, PrincipalTable);
		principal.ShouldContain("SYSTEM_VERSIONING = ON");
		principal.ShouldNotContain("CHECK");
		Creating(commands, FragmentTable).ShouldNotContain("SYSTEM_VERSIONING");
		commands.Where(command => command.Contains("CHECK", StringComparison.Ordinal)).ShouldHaveSingleItem()
			.ShouldBe($"{Add}{Environment.NewLine}");
		string.Join(Environment.NewLine, commands).Split("SYSTEM_VERSIONING").Length.ShouldBe(2);
	}

	[Fact]
	void Adding_a_fragment_check_constraint_to_a_principal_table_only_entity_emits_plain_DDL()
	{
		DiffSql<PrincipalTableOnlyUncheckedAccountsContext, PrincipalTableOnlyCheckedAccountsContext>()
			.ShouldHaveSingleItem()
			.ShouldBe($"{Add}{Environment.NewLine}");
	}

	[Fact]
	void Removing_a_fragment_check_constraint_from_a_principal_table_only_entity_emits_plain_DDL()
	{
		DiffSql<PrincipalTableOnlyCheckedAccountsContext, PrincipalTableOnlyUncheckedAccountsContext>()
			.ShouldHaveSingleItem()
			.ShouldBe($"{Drop}{Environment.NewLine}");
	}

	[Fact]
	void A_scaffolded_add_carries_the_constraint_as_an_annotation_and_replays_it()
	{
		// Layer (a): the operation as written into the migration's C#, then rebuilt from exactly those calls.
		var scaffolded = ScaffoldedCSharp(Operations<UncheckedAccountsContext, CheckedAccountsContext>());
		scaffolded.ShouldContain("AlterTable(");
		scaffolded.ShouldContain($""".Annotation("{AnnotationName}", "{Sql}")""");

		MigrationBuilder migrationBuilder = new(Provider);
		migrationBuilder.AlterTable(name: FragmentTable).Annotation(AnnotationName, Sql);

		ReplayedSql(migrationBuilder.Operations).ShouldBe(Add);
	}

	[Fact]
	void A_scaffolded_drop_carries_the_constraint_as_an_old_annotation_and_replays_it()
	{
		var scaffolded = ScaffoldedCSharp(Operations<CheckedAccountsContext, UncheckedAccountsContext>());
		scaffolded.ShouldContain($""".OldAnnotation("{AnnotationName}", "{Sql}")""");

		MigrationBuilder migrationBuilder = new(Provider);
		migrationBuilder.AlterTable(name: FragmentTable).OldAnnotation(AnnotationName, Sql);

		ReplayedSql(migrationBuilder.Operations).ShouldBe(Drop);
	}

	[Fact]
	void The_model_snapshot_carries_the_constraint_inside_the_fragments_SplitToTable()
	{
		using var context = Create<CheckedAccountsContext>();

		var snapshot = CompiledSnapshot.Code(context, new SqlServerDesignTimeServices());

		var fragment = snapshot.IndexOf($"""SplitToTable("{FragmentTable}" """.TrimEnd(), StringComparison.Ordinal);
		fragment.ShouldBeGreaterThanOrEqualTo(0);
		var annotation = snapshot.IndexOf($"""t.HasAnnotation("{AnnotationName}", "{Sql}")""", StringComparison.Ordinal);
		annotation.ShouldBeGreaterThan(fragment);
		snapshot.Split(AnnotationName).Length.ShouldBe(2, "the constraint is declared once, on the fragment alone");
	}

	[Fact]
	void Fragment_check_constraint_round_trips_through_the_model_snapshot()
	{
		// The snapshot is the source side of the NEXT migration's diff. Compiled and processed as `dotnet ef`
		// processes it, it must leave nothing to diff against the model it was written from.
		using var context = Create<PrincipalTableOnlyCheckedAccountsContext>();
		var code = CompiledSnapshot.Code(context, new SqlServerDesignTimeServices());

		var snapshotModel = CompiledSnapshot.Model(context, new SqlServerDesignTimeServices(), code);

		snapshotModel.GetRelationalModel().FindTable(FragmentTable, null).ShouldNotBeNull()
			.FindAnnotation(AnnotationName).ShouldNotBeNull().Value.ShouldBe(Sql);
		snapshotModel.GetRelationalModel().FindTable(PrincipalTable, null).ShouldNotBeNull()
			.FindAnnotation(AnnotationName).ShouldBeNull();
		DiffSql(snapshotModel, context).ShouldBeEmpty();
	}

	[Fact]
	void A_snapshot_stripped_of_the_annotation_makes_the_next_migration_re_add_the_constraint()
	{
		using var context = Create<CheckedAccountsContext>();
		var code = string.Join('\n', CompiledSnapshot.Code(context, new SqlServerDesignTimeServices()).Split('\n')
			.Where(line => !line.Contains(AnnotationName, StringComparison.Ordinal)));

		var snapshotModel = CompiledSnapshot.Model(context, new SqlServerDesignTimeServices(), code);

		DiffSql(snapshotModel, context).ShouldHaveSingleItem().ShouldBe($"{Add}{Environment.NewLine}");
	}

	[Fact]
	void A_blank_name_or_SQL_is_refused_at_declaration()
	{
		using var blankName = Create<BlankNameContext>();
		using var blankSql = Create<BlankSqlContext>();

		Should.Throw<ArgumentException>(() => blankName.Model).ParamName.ShouldBe("name");
		Should.Throw<ArgumentException>(() => blankSql.Model).ParamName.ShouldBe("sql");
	}

	static string Creating(IReadOnlyList<string> commands, string table) =>
		commands
			.Where(command => command.Contains($"CREATE TABLE [{table}] (", StringComparison.Ordinal))
			.ShouldHaveSingleItem();

	static IReadOnlyList<MigrationOperation> Operations<TFrom, TTo>()
		where TFrom : DbContext
		where TTo : DbContext
	{
		using var from = Create<TFrom>();
		using var to = Create<TTo>();
		return to.GetService<IMigrationsModelDiffer>().GetDifferences(
			from.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
			to.GetService<IDesignTimeModel>().Model.GetRelationalModel());
	}

	static List<string> DiffSql<TFrom, TTo>()
		where TFrom : DbContext
		where TTo : DbContext
	{
		using var from = Create<TFrom>();
		using var to = Create<TTo>();
		return DiffSql(from.GetService<IDesignTimeModel>().Model, to);
	}

	static List<string> DiffSql(IModel source, DbContext to)
	{
		var targetModel = to.GetService<IDesignTimeModel>().Model;
		var operations = to.GetService<IMigrationsModelDiffer>()
			.GetDifferences(source.GetRelationalModel(), targetModel.GetRelationalModel());
		return [.. to.GetService<IMigrationsSqlGenerator>().Generate(operations, targetModel).Select(command => command.CommandText)];
	}

	static string ScaffoldedCSharp(IReadOnlyList<MigrationOperation> operations)
	{
		using var context = Create<CheckedAccountsContext>();
		ServiceCollection services = new();
		services.AddEntityFrameworkDesignTimeServices();
		services.AddDbContextDesignTimeServices(context);
		new SqlServerDesignTimeServices().ConfigureDesignTimeServices(services);
		using var provider = services.BuildServiceProvider();
		IndentedStringBuilder builder = new();
		provider.GetRequiredService<ICSharpMigrationOperationGenerator>().Generate("migrationBuilder", operations, builder);
		return builder.ToString();
	}

	static string ReplayedSql(IReadOnlyList<MigrationOperation> operations)
	{
		using var context = Create<CheckedAccountsContext>();
		var commands = context.GetService<IMigrationsSqlGenerator>()
			.Generate(operations, context.GetService<IDesignTimeModel>().Model);
		return string.Join(Environment.NewLine, commands.Select(command => command.CommandText.TrimEnd()));
	}

	static TContext Create<TContext>() where TContext : DbContext
	{
		DbContextOptionsBuilder<TContext> optionsBuilder = new();
		optionsBuilder.ApplyNorseProviderOptions(NorseSqlServerEfProvider.Instance,
			NorseSqlServerEfProvider.Instance.DesignTimePlaceholderConnectionString("norse_test"),
			migrationsAssemblyName: null);
		return Activator.CreateInstance(typeof(TContext), optionsBuilder.Options) as TContext
			?? throw new InvalidOperationException($"{typeof(TContext).Name} takes its options as its only argument.");
	}

	sealed class NoAccountsContext(DbContextOptions<NoAccountsContext> options) : NorseDbContext(options);

	// Every variant maps the same table pair, Accounts split to AccountCredentials; each context type is its own
	// cached model and overrides only the one thing its migration changes.
	abstract class AccountsContext<TAccount>(DbContextOptions options) : NorseDbContext(options)
		where TAccount : AccountShape, INorseEntity<TAccount>
	{
		protected virtual void Credentials(SplitTableBuilder<TAccount> credentials) { }

		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			var account = builder.Entity<TAccount>();
			TAccount.Configure(account);
			account
				.ToTable(PrincipalTable)
				.SplitToTable(FragmentTable, credentials =>
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
			credentials.HasFragmentCheckConstraint(Constraint, Sql);
	}

	sealed class RecheckedAccountsContext(DbContextOptions<RecheckedAccountsContext> options)
		: AccountsContext<Account>(options)
	{
		protected override void Credentials(SplitTableBuilder<Account> credentials) =>
			credentials.HasFragmentCheckConstraint(Constraint, ReworkedSql);
	}

	sealed class PrincipalTableOnlyUncheckedAccountsContext(
		DbContextOptions<PrincipalTableOnlyUncheckedAccountsContext> options)
		: AccountsContext<PrincipalTableOnlyAccount>(options);

	sealed class PrincipalTableOnlyCheckedAccountsContext(
		DbContextOptions<PrincipalTableOnlyCheckedAccountsContext> options)
		: AccountsContext<PrincipalTableOnlyAccount>(options)
	{
		protected override void Credentials(SplitTableBuilder<PrincipalTableOnlyAccount> credentials) =>
			credentials.HasFragmentCheckConstraint(Constraint, Sql);
	}

	// The same expression declared the only way EF offers: an entity-level check constraint.
	sealed class EntityLevelCheckedAccountsContext(DbContextOptions<EntityLevelCheckedAccountsContext> options)
		: AccountsContext<Account>(options)
	{
		protected override void OnModelCreating(ModelBuilder builder)
		{
			base.OnModelCreating(builder);
			builder.Entity<Account>().ToTable(table => table.HasCheckConstraint(Constraint, Sql));
		}
	}

	sealed class BlankNameContext(DbContextOptions<BlankNameContext> options) : AccountsContext<Account>(options)
	{
		protected override void Credentials(SplitTableBuilder<Account> credentials) =>
			credentials.HasFragmentCheckConstraint(" ", Sql);
	}

	sealed class BlankSqlContext(DbContextOptions<BlankSqlContext> options) : AccountsContext<Account>(options)
	{
		protected override void Credentials(SplitTableBuilder<Account> credentials) =>
			credentials.HasFragmentCheckConstraint(Constraint, "");
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

	sealed record PrincipalTableOnlyAccount : AccountShape, ITemporalEntity, INorseEntity<PrincipalTableOnlyAccount>
	{
		public static void Configure(EntityTypeBuilder<PrincipalTableOnlyAccount> builder) =>
			builder.TemporalOnPrincipalTableOnly();
	}
}
