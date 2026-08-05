using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

// EF1001: the design-time service registration (DesignTimeServiceCollectionExtensions), the migrations
// code-generator selector, and Npgsql's design-time services are all EF-internal by attribute. They are
// also precisely what `dotnet ef migrations add` runs, and this suite exists to measure that path rather
// than a reimplementation of it, so the internal surface is used deliberately.
#pragma warning disable EF1001

/// <summary>
/// The real adoption path, end to end: <c>dotnet ef migrations add</c> and then <c>database update</c>.
/// Every other temporal suite drives the differ straight into the generator, which skips the two
/// serialization layers a scaffolded migration actually goes through — and the
/// <see cref="NorseAnnotationNames.Temporal"/> marker has to survive both of them.
/// </summary>
/// <remarks>
/// <para>
/// Layer (a) is the operation annotations: written into the scaffolded C# and reconstructed through
/// <see cref="MigrationBuilder"/> at apply time. Measured twice — EF's real
/// <see cref="ICSharpMigrationOperationGenerator"/> has to emit the <c>.Annotation</c>/<c>.OldAnnotation</c>
/// call, and operations rebuilt from exactly those calls have to still drive the apparatus. The
/// enable/disable <see cref="AlterTableOperation"/> and the <c>ForRemove</c>-sourced
/// <see cref="DropTableOperation"/> live or die here.
/// </para>
/// <para>
/// Layer (b) is the entity-type annotation surviving into the migration's designer <c>BuildTargetModel</c>
/// and into the snapshot. That model — a bare convention set, the shape written longhand, then
/// <see cref="IModelRuntimeInitializer"/>, which is the call EF's own <c>Migrator</c> makes before handing
/// the target model to the SQL generator — is what the generator consults for a create and for column
/// operations, neither of which carries a marker of its own. A drop there is silent: appliable SQL, no
/// error anywhere, and a temporal table left without its history mirror.
/// </para>
/// <para>
/// Compiling and executing the scaffolded C# is the heavyweight equivalent; this measures the same two
/// joints without a Roslyn build. Applying the result is <see cref="TemporalEvolutionLiveTests"/>'s job —
/// here the SQL text is the measurement.
/// </para>
/// </remarks>
public sealed class TemporalScaffoldRoundTripTests
{
	const string Provider = "Npgsql.EntityFrameworkCore.PostgreSQL";

	// ---------------------------------------------------------------------------------------------
	// Layer (a) — the marker written into the scaffolded C# migration.
	// ---------------------------------------------------------------------------------------------

	[Fact]
	void The_enable_transition_scaffolds_the_marker_as_an_annotation_call()
	{
		// Without this the scaffolded migration is an AlterTable that says nothing at all, and applying it
		// builds no apparatus.
		var scaffolded = ScaffoldedCSharp(TemporalEvolution.EnableOperations());

		scaffolded.ShouldContain("AlterTable(");
		scaffolded.ShouldContain(""".Annotation("Norse:Temporal", true)""");
	}

	[Fact]
	void The_disable_transition_scaffolds_the_marker_as_an_old_annotation_call()
	{
		// On the disable side the target model still holds the table but no longer marks it, so OldTable is
		// the only record that it ever was temporal — and OldTable reaches the scaffolded C# only as
		// .OldAnnotation.
		var scaffolded = ScaffoldedCSharp(TemporalEvolution.DisableOperations());

		scaffolded.ShouldContain("AlterTable(");
		scaffolded.ShouldContain(""".OldAnnotation("Norse:Temporal", true)""");
	}

	[Fact]
	void Dropping_the_entity_scaffolds_the_marker_onto_the_drop_table_call()
	{
		// The DropTableOperation's marker comes from IMigrationsAnnotationProvider.ForRemove and has
		// nowhere else to live: the entity has left the target model entirely.
		var scaffolded = ScaffoldedCSharp(TemporalEvolution.DropEntityOperations());

		scaffolded.ShouldContain("DropTable(");
		scaffolded.ShouldContain(""".Annotation("Norse:Temporal", true)""");
	}

	[Fact]
	void A_created_table_scaffolds_the_marker_even_though_the_generator_does_not_read_it()
	{
		// Measured, not assumed: EF's differ copies the relational table's annotations onto the create
		// operation, so the marker does reach the scaffolded C#. The generator's create path ignores it and
		// consults the target model anyway — see the neuter below, which leaves this annotation in place and
		// still gets no apparatus out of an unmarked model.
		ScaffoldedCSharp(TemporalEvolution.CreateOperations())
			.ShouldContain(""".Annotation("Norse:Temporal", true)""");
	}

	[Fact]
	void An_added_column_scaffolds_no_marker_and_has_nowhere_to_put_one()
	{
		// Column operations are the shape with no marker of their own anywhere in the scaffolded migration,
		// which makes layer (b) the only thing standing between a temporal table and a missing history
		// mirror. Asserted so the division of labor stays honest.
		ScaffoldedCSharp(TemporalEvolution.AddColumnOperations()).ShouldNotContain("Norse:Temporal");
	}

	[Fact]
	void A_reconstructed_enable_transition_is_indistinguishable_from_the_differ_driven_one()
	{
		// The whole of layer (a) in one assertion: operations rebuilt from the MigrationBuilder calls the
		// scaffolded C# carries produce character-for-character the SQL the differ's own operations do.
		ReconstructedSql(EnableTransition(withMarker: true), RebuiltTargetModel(marked: true))
			.ShouldBe(TemporalEvolution.EnableSql());
	}

	[Fact]
	void A_reconstructed_enable_transition_still_builds_the_whole_apparatus()
	{
		var sql = ReconstructedSql(EnableTransition(withMarker: true), RebuiltTargetModel(marked: true));

		sql.ShouldContain("ADD COLUMN system_period tstzrange");
		sql.ShouldContain("""CREATE TABLE "public"."temporal_widgets_history""");
		sql.ShouldContain("""PRIMARY KEY ("id", "system_period" WITHOUT OVERLAPS)""");
		sql.ShouldContain("""CREATE FUNCTION "public"."temporal_widgets_versioning"()""");
		sql.ShouldContain("""CREATE TRIGGER "temporal_widgets_versioning_update""");
		sql.ShouldContain("""CREATE VIEW "public"."temporal_widgets_timeline""");
	}

	[Fact]
	void A_reconstructed_entity_drop_still_tears_the_apparatus_down_before_the_table()
	{
		var sql = ReconstructedSql(EntityDrop(withMarker: true), RebuiltTargetModel(marked: false, withTable: false));

		var dropView = Position(sql, """DROP VIEW "public"."temporal_widgets_timeline";""");
		var dropFunction = Position(sql, """DROP FUNCTION "public"."temporal_widgets_versioning"();""");
		var dropHistory = Position(sql, """DROP TABLE "public"."temporal_widgets_history";""");
		var dropMain = Position(sql, "DROP TABLE public.temporal_widgets;");

		dropView.ShouldBeLessThan(dropFunction);
		dropFunction.ShouldBeLessThan(dropHistory);
		dropHistory.ShouldBeLessThan(dropMain);
	}

	// ---------------------------------------------------------------------------------------------
	// Layer (b) — the marker written into the designer file and the snapshot, and read back at apply time.
	// ---------------------------------------------------------------------------------------------

	[Fact]
	void The_designer_files_build_target_model_carries_the_marker()
	{
		// BuildTargetModel is what the generator consults at `database update` time for every operation
		// that carries no marker of its own.
		using MarkedContext context = new(TemporalEvolution.Options<MarkedContext>());

		var designer = DesignTimeCode(context, generator => generator.GenerateMetadata(
			"Norse.Test.Migrations", context.GetType(), "AddTemporalWidgets",
			"20260805000000_AddTemporalWidgets", context.GetService<IDesignTimeModel>().Model));

		designer.ShouldContain("BuildTargetModel");
		designer.ShouldContain("""HasAnnotation("Norse:Temporal", true)""");
	}

	[Fact]
	void The_model_snapshot_carries_the_marker_too()
	{
		// The snapshot is the source side of the NEXT migration's diff. A marker missing here reads as
		// "temporality was just enabled" on every subsequent scaffold.
		using MarkedContext context = new(TemporalEvolution.Options<MarkedContext>());

		var snapshot = DesignTimeCode(context, generator => generator.GenerateSnapshot(
			"Norse.Test.Migrations", context.GetType(), "MarkedContextModelSnapshot",
			context.GetService<IDesignTimeModel>().Model, "20260805000000_AddTemporalWidgets"));

		snapshot.ShouldContain("""HasAnnotation("Norse:Temporal", true)""");
	}

	[Fact]
	void An_unmarked_entity_puts_no_marker_in_the_snapshot()
	{
		using UnmarkedContext context = new(TemporalEvolution.Options<UnmarkedContext>());

		var snapshot = DesignTimeCode(context, generator => generator.GenerateSnapshot(
			"Norse.Test.Migrations", context.GetType(), "UnmarkedContextModelSnapshot",
			context.GetService<IDesignTimeModel>().Model, "20260805000000_AddPlainWidgets"));

		snapshot.ShouldNotContain("Norse:Temporal");
	}

	[Fact]
	void The_rebuilt_target_model_projects_the_marker_onto_its_relational_table()
	{
		// The designer-style model never meets a Norse convention — no ITemporalEntity, no stamping pass.
		// The annotation it replays has to reach the relational table through the annotation provider on
		// its own, or every marker-free operation below silently loses its apparatus.
		var table = RebuiltTargetModel(marked: true).GetRelationalModel().Tables
			.Single(candidate => candidate.Name == TemporalEvolution.Table);

		table.FindAnnotation(NorseAnnotationNames.Temporal).ShouldNotBeNull().Value.ShouldBe(true);
	}

	[Fact]
	void A_reconstructed_create_against_the_rebuilt_model_still_builds_the_apparatus()
	{
		// CreateTableOperation carries no marker; everything here is read off the rebuilt target model.
		var sql = ReconstructedSql(CreateTable(), RebuiltTargetModel(marked: true));

		sql.ShouldContain("""ADD COLUMN system_period tstzrange NOT NULL;""");
		sql.ShouldContain("""CREATE TABLE "public"."temporal_widgets_history""");
		sql.ShouldContain("""PRIMARY KEY ("id", "system_period" WITHOUT OVERLAPS)""");
		sql.ShouldContain("""CREATE VIEW "public"."temporal_widgets_timeline""");
	}

	[Fact]
	void A_reconstructed_added_column_against_the_rebuilt_model_still_mirrors_onto_history()
	{
		// The silent failure this round trip exists to catch: an AddColumn on a temporal table whose marker
		// did not survive into the designer model applies cleanly and leaves history one column behind.
		var sql = ReconstructedSql(AddColumn(), RebuiltTargetModel(marked: true, withExtraColumn: true));

		var dropView = Position(sql, """DROP VIEW "public"."temporal_widgets_timeline";""");
		var historyAdd = Position(sql,
			"""ALTER TABLE "public"."temporal_widgets_history" ADD COLUMN "extra" character varying(50);""");
		var function = Position(sql, "CREATE OR REPLACE FUNCTION");
		var createView = Position(sql, """CREATE VIEW "public"."temporal_widgets_timeline""");

		dropView.ShouldBeLessThan(historyAdd);
		historyAdd.ShouldBeLessThan(function);
		function.ShouldBeLessThan(createView);
	}

	// ---------------------------------------------------------------------------------------------
	// The neuter: strip the marker from each layer in turn and the apparatus vanishes without a word.
	// These are the RED this suite was written against, kept standing as the proof that the assertions
	// above measure the marker and not something incidental.
	// ---------------------------------------------------------------------------------------------

	[Fact]
	void An_enable_transition_stripped_of_its_annotation_emits_no_apparatus()
	{
		var sql = ReconstructedSql(EnableTransition(withMarker: false), RebuiltTargetModel(marked: true));

		sql.ShouldNotContain("system_period");
		sql.ShouldNotContain("_history");
		sql.ShouldNotContain("_timeline");
	}

	[Fact]
	void An_entity_drop_stripped_of_its_annotation_leaves_the_apparatus_behind()
	{
		var sql = ReconstructedSql(EntityDrop(withMarker: false), RebuiltTargetModel(marked: false, withTable: false));

		sql.ShouldContain("DROP TABLE public.temporal_widgets;");
		sql.ShouldNotContain("_history");
		sql.ShouldNotContain("_timeline");
		sql.ShouldNotContain("_versioning");
	}

	[Fact]
	void A_create_against_a_target_model_stripped_of_the_marker_emits_no_apparatus()
	{
		// The operation still carries its own .Annotation("Norse:Temporal", true) — the create path does not
		// read it. Only the rebuilt target model decides, which is exactly why layer (b) has to hold.
		var sql = ReconstructedSql(CreateTable(), RebuiltTargetModel(marked: false));

		sql.ShouldContain("CREATE TABLE public.temporal_widgets");
		sql.ShouldNotContain("system_period");
		sql.ShouldNotContain("_history");
	}

	[Fact]
	void A_column_added_against_a_target_model_stripped_of_the_marker_mirrors_nothing()
	{
		// This is the silent one: appliable SQL, no error, no history mirror.
		var sql = ReconstructedSql(AddColumn(), RebuiltTargetModel(marked: false, withExtraColumn: true));

		sql.ShouldContain("ALTER TABLE public.temporal_widgets ADD extra");
		sql.ShouldNotContain("_history");
		sql.ShouldNotContain("_timeline");
	}

	// ---------------------------------------------------------------------------------------------
	// Layer (a) apparatus: EF's real design-time codegen, and the MigrationBuilder calls it writes out.
	// ---------------------------------------------------------------------------------------------

	// The design-time service provider `dotnet ef` builds for itself: EF's own design-time services, the
	// context's, and the provider's.
	static ServiceProvider DesignTimeServices(DbContext context)
	{
		ServiceCollection services = new();
		services.AddEntityFrameworkDesignTimeServices();
		services.AddDbContextDesignTimeServices(context);
		new NpgsqlDesignTimeServices().ConfigureDesignTimeServices(services);
		return services.BuildServiceProvider();
	}

	// The C# body of the scaffolded migration's Up method — the text `dotnet ef migrations add` writes to
	// disk, produced by EF's real operation generator rather than reasoned about.
	static string ScaffoldedCSharp(IReadOnlyList<MigrationOperation> operations)
	{
		using MarkedContext context = new(TemporalEvolution.Options<MarkedContext>());
		using var services = DesignTimeServices(context);
		IndentedStringBuilder builder = new();
		services.GetRequiredService<ICSharpMigrationOperationGenerator>()
			.Generate("migrationBuilder", operations, builder);
		return builder.ToString();
	}

	static string DesignTimeCode(DbContext context, Func<IMigrationsCodeGenerator, string> generate)
	{
		using var services = DesignTimeServices(context);
		return generate(services.GetRequiredService<IMigrationsCodeGeneratorSelector>().Select(language: null));
	}

	// Exactly the calls the scaffolded C# carries, replayed through the same MigrationBuilder a migration's
	// Up method uses — the reconstruction half of layer (a).
	static IReadOnlyList<MigrationOperation> EnableTransition(bool withMarker)
	{
		MigrationBuilder migrationBuilder = new(Provider);
		var alterTable = migrationBuilder.AlterTable(
			name: TemporalEvolution.Table,
			schema: TemporalEvolution.Schema);
		if (withMarker)
			alterTable.Annotation(NorseAnnotationNames.Temporal, true);
		return migrationBuilder.Operations;
	}

	static IReadOnlyList<MigrationOperation> EntityDrop(bool withMarker)
	{
		MigrationBuilder migrationBuilder = new(Provider);
		var dropTable = migrationBuilder.DropTable(
			name: TemporalEvolution.Table,
			schema: TemporalEvolution.Schema);
		if (withMarker)
			dropTable.Annotation(NorseAnnotationNames.Temporal, true);
		return migrationBuilder.Operations;
	}

	// Faithful to the scaffolded C# above, marker annotation and all — which the create path deliberately
	// does not read: it consults the target model, so this operation paired with an unmarked rebuilt model
	// is what proves layer (b) is load-bearing here rather than decorative.
	static IReadOnlyList<MigrationOperation> CreateTable()
	{
		MigrationBuilder migrationBuilder = new(Provider);
		migrationBuilder.CreateTable(
			name: TemporalEvolution.Table,
			schema: TemporalEvolution.Schema,
			columns: table => new
			{
				Id = table.Column<int>(name: "id", type: "integer", nullable: false),
				Name = table.Column<string>(name: "name", type: "character varying(100)", maxLength: 100,
					nullable: false)
			},
			constraints: table => table.PrimaryKey("pk_temporal_widgets", columns => columns.Id))
			.Annotation(NorseAnnotationNames.Temporal, true);
		return migrationBuilder.Operations;
	}

	static IReadOnlyList<MigrationOperation> AddColumn()
	{
		MigrationBuilder migrationBuilder = new(Provider);
		migrationBuilder.AddColumn<string>(
			name: "extra",
			schema: TemporalEvolution.Schema,
			table: TemporalEvolution.Table,
			type: "character varying(50)",
			maxLength: 50,
			nullable: false,
			defaultValue: "");
		return migrationBuilder.Operations;
	}

	// ---------------------------------------------------------------------------------------------
	// Layer (b) apparatus: the designer file's model, rebuilt the way EF rebuilds it at apply time.
	// ---------------------------------------------------------------------------------------------

	/// <summary>
	/// A model built the way a migration's designer file builds one: a bare convention set — no Norse
	/// conventions, no <c>ITemporalEntity</c>, no stamping pass — the shape written out longhand, then
	/// <see cref="IModelRuntimeInitializer"/>, which is the call EF's own <c>Migrator</c> makes before it
	/// hands the target model to the SQL generator. The single <c>HasAnnotation</c> line is the one thing
	/// the designer file has to have carried across for any of this to work.
	/// </summary>
	static IModel RebuiltTargetModel(bool marked, bool withExtraColumn = false, bool withTable = true)
	{
		ModelBuilder modelBuilder = new(new ConventionSet());
		modelBuilder.HasDefaultSchema(TemporalEvolution.Schema);
		if (withTable)
			modelBuilder.Entity("TemporalWidget", entity => BuildTemporalWidget(entity, marked, withExtraColumn));

		using MarkedContext context = new(TemporalEvolution.Options<MarkedContext>());
		return context.GetService<IModelRuntimeInitializer>()
			.Initialize(modelBuilder.FinalizeModel(), designTime: true, validationLogger: null);
	}

	static void BuildTemporalWidget(EntityTypeBuilder entity, bool marked, bool withExtraColumn)
	{
		entity.Property<int>("Id").HasColumnName("id").HasColumnType("integer");
		entity.Property<string>("Name").HasColumnName("name").HasColumnType("character varying(100)")
			.HasMaxLength(100).IsRequired();
		if (withExtraColumn)
			entity.Property<string>("Extra").HasColumnName("extra").HasColumnType("character varying(50)")
				.HasMaxLength(50).IsRequired();
		entity.HasKey("Id").HasName("pk_temporal_widgets");
		entity.ToTable(TemporalEvolution.Table, TemporalEvolution.Schema);
		if (marked)
			entity.HasAnnotation(NorseAnnotationNames.Temporal, true);
	}

	// The real generator, off a real Norse context, over reconstructed operations and a rebuilt model —
	// the two halves of the round trip meeting where `database update` puts them.
	static string ReconstructedSql(IReadOnlyList<MigrationOperation> operations, IModel targetModel)
	{
		using MarkedContext context = new(TemporalEvolution.Options<MarkedContext>());
		var commands = context.GetService<IMigrationsSqlGenerator>().Generate(operations, targetModel);
		return string.Join(Environment.NewLine, commands.Select(command => command.CommandText));
	}

	static int Position(string sql, string statement)
	{
		var position = sql.IndexOf(statement, StringComparison.Ordinal);
		position.ShouldBeGreaterThanOrEqualTo(0, $"'{statement}' should have been emitted");
		return position;
	}
}
