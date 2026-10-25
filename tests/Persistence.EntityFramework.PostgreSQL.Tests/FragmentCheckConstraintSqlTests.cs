using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal;
using static Norse.Persistence.EntityFramework.PostgreSQL.Tests.FragmentChecks;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

// EF1001: Npgsql's design-time services and EF's design-time registration are EF-internal by attribute; they are
// what `dotnet ef migrations add` runs, which is the path the scaffold and snapshot facts measure.
#pragma warning disable EF1001

/// <summary>
///     <c>HasFragmentCheckConstraint</c> on PostgreSQL, through EF's real model differ and the generator exactly as
///     <see cref="NorsePostgresEfProvider" /> wires it: no hand-built operation and no database. The constraint has
///     to land on the fragment's table — EF's own entity-level check constraint lands on the principal's, where
///     the columns it names do not exist — and has to survive the two serialization layers a scaffolded migration
///     goes through: the migration's own operation calls and the model snapshot.
/// </summary>
public sealed class FragmentCheckConstraintSqlTests
{
	const string Provider = "Npgsql.EntityFrameworkCore.PostgreSQL";

	const string Add = $"ALTER TABLE {FragmentTable} ADD CONSTRAINT {Constraint} CHECK ({Sql});";

	const string Drop = $"ALTER TABLE {FragmentTable} DROP CONSTRAINT {Constraint};";

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

		Creating(commands, PrincipalTable).ShouldContain($"CONSTRAINT {Constraint} CHECK ({Sql})");
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
			$"ALTER TABLE {FragmentTable} ADD CONSTRAINT {Constraint} CHECK ({ReworkedSql});{Environment.NewLine}"
		]);
	}

	[Fact]
	void An_unchanged_fragment_check_constraint_emits_nothing()
	{
		DiffSql<CheckedAccountsContext, CheckedAccountsContext>().ShouldBeEmpty();
	}

	[Fact]
	void The_fragment_of_a_temporal_entity_takes_the_constraint_and_no_apparatus()
	{
		var sql = string.Join(Environment.NewLine, DiffSql<NoAccountsContext, TemporalCheckedAccountsContext>());

		sql.ShouldContain(Add);
		sql.ShouldContain($"""CREATE TABLE "public"."{PrincipalTable}_history" """.TrimEnd());
		sql.ShouldNotContain($"{FragmentTable}_history");
		sql.ShouldNotContain($"{FragmentTable}_versioning");
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
	void A_scaffolded_create_carries_the_constraint_and_replays_it_after_the_table()
	{
		var scaffolded = ScaffoldedCSharp(Operations<NoAccountsContext, CheckedAccountsContext>());
		scaffolded.ShouldContain($""".Annotation("{AnnotationName}", "{Sql}")""");

		MigrationBuilder migrationBuilder = new(Provider);
		migrationBuilder.CreateTable(
				name: FragmentTable,
				columns: table => new
				{
					Id = table.Column<int>(name: "id", type: "integer", nullable: false),
					NormalizedEmail = table.Column<string>(name: "normalized_email", type: "character varying(256)",
						maxLength: 256, nullable: false),
					NormalizedUserName = table.Column<string>(name: "normalized_user_name",
						type: "character varying(256)", maxLength: 256, nullable: false)
				},
				constraints: table => table.PrimaryKey("PK_account_credentials", columns => columns.Id))
			.Annotation(AnnotationName, Sql);

		var sql = ReplayedSql(migrationBuilder.Operations);
		sql.IndexOf($"CREATE TABLE {FragmentTable}", StringComparison.Ordinal)
			.ShouldBeLessThan(sql.IndexOf(Add, StringComparison.Ordinal));
	}

	[Fact]
	void The_model_snapshot_carries_the_constraint_inside_the_fragments_SplitToTable()
	{
		using CheckedAccountsContext context = new(TemporalEvolution.Options<CheckedAccountsContext>());

		var snapshot = CompiledSnapshot.Code(context, new NpgsqlDesignTimeServices());

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
		// processes it, it must leave nothing to diff against the model it was written from — a constraint lost
		// in the round trip would be re-added by every subsequent migration.
		using CheckedAccountsContext context = new(TemporalEvolution.Options<CheckedAccountsContext>());
		var code = CompiledSnapshot.Code(context, new NpgsqlDesignTimeServices());

		var snapshotModel = CompiledSnapshot.Model(context, new NpgsqlDesignTimeServices(), code);

		snapshotModel.GetRelationalModel().FindTable(FragmentTable, null).ShouldNotBeNull()
			.FindAnnotation(AnnotationName).ShouldNotBeNull().Value.ShouldBe(Sql);
		snapshotModel.GetRelationalModel().FindTable(PrincipalTable, null).ShouldNotBeNull()
			.FindAnnotation(AnnotationName).ShouldBeNull();
		DiffSql(snapshotModel, context).ShouldBeEmpty();
	}

	[Fact]
	void A_snapshot_stripped_of_the_annotation_makes_the_next_migration_re_add_the_constraint()
	{
		// The neuter: the annotation line is what carries the constraint across, and nothing else does.
		using CheckedAccountsContext context = new(TemporalEvolution.Options<CheckedAccountsContext>());
		var code = string.Join('\n', CompiledSnapshot.Code(context, new NpgsqlDesignTimeServices()).Split('\n')
			.Where(line => !line.Contains(AnnotationName, StringComparison.Ordinal)));

		var snapshotModel = CompiledSnapshot.Model(context, new NpgsqlDesignTimeServices(), code);

		DiffSql(snapshotModel, context).ShouldHaveSingleItem().ShouldBe($"{Add}{Environment.NewLine}");
	}

	static string Creating(IReadOnlyList<string> commands, string table) =>
		commands
			.Where(command => command.Contains($"CREATE TABLE {table} (", StringComparison.Ordinal))
			.ShouldHaveSingleItem();

	static IReadOnlyList<MigrationOperation> Operations<TFrom, TTo>()
		where TFrom : DbContext
		where TTo : DbContext
	{
		using var from = Create<TFrom>();
		using var to = Create<TTo>();
		return TemporalEvolution.Operations(from, to);
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
		new NpgsqlDesignTimeServices().ConfigureDesignTimeServices(services);
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

	static TContext Create<TContext>() where TContext : DbContext =>
		Activator.CreateInstance(typeof(TContext), TemporalEvolution.Options<TContext>()) as TContext
		?? throw new InvalidOperationException($"{typeof(TContext).Name} takes its options as its only argument.");
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
