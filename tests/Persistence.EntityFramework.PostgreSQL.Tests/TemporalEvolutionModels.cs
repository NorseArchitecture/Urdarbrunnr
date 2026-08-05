using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

/// <summary>
/// The model variants and the differ-driven arrange shared by the evolution snapshot tests and the
/// live application tests. One set of models, one transition helper: the SQL a snapshot test asserts
/// on is character-for-character the SQL the live test applies to PostgreSQL, so a snapshot that goes
/// green on unappliable DDL cannot survive both suites (spec ruling 16).
/// </summary>
/// <remarks>
/// Every context declares <c>public</c> as its default schema. Npgsql leaves an undeclared schema to
/// the session search path and emits the main table unqualified, while the apparatus must be
/// qualified for the <c>SECURITY DEFINER</c> function — declaring the schema keeps both sides of
/// every mirror statement in one namespace, which is what an evolution test is actually about.
/// </remarks>
static class TemporalEvolution
{
	public const string Schema = "public";

	public const string Table = "temporal_widgets";

	public const string RenamedTable = "renamed_widgets";

	public const string OtherSchema = "archive";

	public const string PlainTable = "plain_widgets";

	/// <summary>A column added to the marked table: <c>AddColumn</c> forward, <c>DropColumn</c> back.</summary>
	public static string AddColumnSql()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using MarkedPlusContext to = new(Options<MarkedPlusContext>());
		return TransitionSql(from, to);
	}

	/// <summary>The same pair read backwards — the added column drops again.</summary>
	public static string DropColumnSql()
	{
		using MarkedPlusContext from = new(Options<MarkedPlusContext>());
		using MarkedContext to = new(Options<MarkedContext>());
		return TransitionSql(from, to);
	}

	/// <summary>
	/// Two columns arriving in one batch. The differ pairs columns by name first and by mapped property
	/// name second, so the same entity with a re-pointed column name is a rename, and a wider entity is
	/// a pair of adds — this is the multi-operation batch the fixed order has to survive.
	/// </summary>
	public static string TwoAddedColumnsSql()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using MarkedDoublePlusContext to = new(Options<MarkedDoublePlusContext>());
		return TransitionSql(from, to);
	}

	public static string RenameColumnSql()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using MarkedRenamedColumnContext to = new(Options<MarkedRenamedColumnContext>());
		return TransitionSql(from, to);
	}

	public static string AlterColumnTypeSql()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using MarkedWiderContext to = new(Options<MarkedWiderContext>());
		return TransitionSql(from, to);
	}

	public static string RenameTableSql()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using MarkedRenamedTableContext to = new(Options<MarkedRenamedTableContext>());
		return TransitionSql(from, to);
	}

	/// <summary>
	/// A rename and a column add in one migration — the shape where the choreography and the fixed column
	/// order have to agree with each other, since both touch the same timeline view.
	/// </summary>
	public static string RenameTableWithAddedColumnSql()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using MarkedRenamedTablePlusContext to = new(Options<MarkedRenamedTablePlusContext>());
		return TransitionSql(from, to);
	}

	/// <summary>The same collision read the other way: a rename whose batch also drops a column.</summary>
	public static string RenameTableWithDroppedColumnSql()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using MarkedRenamedTableMinusContext to = new(Options<MarkedRenamedTableMinusContext>());
		return TransitionSql(from, to);
	}

	/// <summary>
	/// Both sides at once: a rename whose batch drops a column ahead of it and adds one after it. EF sorts
	/// the drop before the rename (old table name) and the add after it (new table name), so this is the
	/// shape where a per-operation-name grouping would see two groups and finish the first one early.
	/// </summary>
	public static string RenameTableWithDroppedAndAddedColumnSql()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using MarkedRenamedTableSwappedColumnContext to =
			new(Options<MarkedRenamedTableSwappedColumnContext>());
		return TransitionSql(from, to);
	}

	/// <summary>
	/// The entity leaves the model entirely (spec §3.4, "dropping the entity"). The target model has no
	/// such table at all, so nothing but the operation itself records that it was ever temporal.
	/// </summary>
	public static string DropEntitySql()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using EmptyContext to = new(Options<EmptyContext>());
		return TransitionSql(from, to);
	}

	/// <summary>The same shape on an unmarked entity: Npgsql's own bare <c>DROP TABLE</c>, untouched.</summary>
	public static string PlainDropEntitySql()
	{
		using PlainContext from = new(Options<PlainContext>());
		using EmptyContext to = new(Options<EmptyContext>());
		return TransitionSql(from, to);
	}

	public static string KeyChangeSql()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using MarkedOtherKeyContext to = new(Options<MarkedOtherKeyContext>());
		return TransitionSql(from, to);
	}

	public static string SchemaMoveSql()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using MarkedOtherSchemaContext to = new(Options<MarkedOtherSchemaContext>());
		return TransitionSql(from, to);
	}

	public static string PlainAddColumnSql()
	{
		using PlainContext from = new(Options<PlainContext>());
		using PlainPlusContext to = new(Options<PlainPlusContext>());
		return TransitionSql(from, to);
	}

	public static string PlainRenameTableSql()
	{
		using PlainContext from = new(Options<PlainContext>());
		using PlainRenamedTableContext to = new(Options<PlainRenamedTableContext>());
		return TransitionSql(from, to);
	}

	public static string PlainKeyChangeSql()
	{
		using PlainContext from = new(Options<PlainContext>());
		using PlainOtherKeyContext to = new(Options<PlainOtherKeyContext>());
		return TransitionSql(from, to);
	}

	/// <summary>The table gains temporality: the same table under an unmarked entity, then a marked one.</summary>
	public static string EnableSql()
	{
		using UnmarkedContext from = new(Options<UnmarkedContext>());
		using MarkedContext to = new(Options<MarkedContext>());
		return TransitionSql(from, to);
	}

	// The real differ over two real models, then the real generator over what it produced: no operation
	// in this suite is ever hand-built, so a change in how EF surfaces one fails here rather than
	// passing against a fabrication.
	public static string TransitionSql(DbContext from, DbContext to)
	{
		var targetModel = to.GetService<IDesignTimeModel>().Model;
		var commands = to.GetService<IMigrationsSqlGenerator>().Generate(Operations(from, to), targetModel);
		return string.Join(Environment.NewLine, commands.Select(command => command.CommandText));
	}

	/// <summary>
	/// The differ's own output, before the generator sees it — the arrange the scaffold round-trip needs,
	/// since it measures what survives being written out as C# and rebuilt, not what the generator makes
	/// of the in-memory operations.
	/// </summary>
	public static IReadOnlyList<MigrationOperation> Operations(DbContext from, DbContext to) =>
		to.GetService<IMigrationsModelDiffer>().GetDifferences(
			from.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
			to.GetService<IDesignTimeModel>().Model.GetRelationalModel());

	/// <summary>The four shapes the scaffold round-trip measures, each read straight off the real differ.</summary>
	public static IReadOnlyList<MigrationOperation> CreateOperations()
	{
		using EmptyContext from = new(Options<EmptyContext>());
		using MarkedContext to = new(Options<MarkedContext>());
		return Operations(from, to);
	}

	public static IReadOnlyList<MigrationOperation> EnableOperations()
	{
		using UnmarkedContext from = new(Options<UnmarkedContext>());
		using MarkedContext to = new(Options<MarkedContext>());
		return Operations(from, to);
	}

	public static IReadOnlyList<MigrationOperation> DisableOperations()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using UnmarkedContext to = new(Options<UnmarkedContext>());
		return Operations(from, to);
	}

	public static IReadOnlyList<MigrationOperation> AddColumnOperations()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using MarkedPlusContext to = new(Options<MarkedPlusContext>());
		return Operations(from, to);
	}

	public static IReadOnlyList<MigrationOperation> DropEntityOperations()
	{
		using MarkedContext from = new(Options<MarkedContext>());
		using EmptyContext to = new(Options<EmptyContext>());
		return Operations(from, to);
	}

	/// <summary>Design-time options: the placeholder connection string, never dialed.</summary>
	public static DbContextOptions<TContext> Options<TContext>() where TContext : DbContext =>
		LiveOptions<TContext>(
			NorsePostgresEfProvider.Instance.DesignTimePlaceholderConnectionString("norse_test"));

	/// <summary>Options against a real database — the same provider wiring, a real connection string.</summary>
	public static DbContextOptions<TContext> LiveOptions<TContext>(string connectionString)
		where TContext : DbContext
	{
		DbContextOptionsBuilder<TContext> optionsBuilder = new();
		optionsBuilder.ApplyNorseProviderOptions(NorsePostgresEfProvider.Instance, connectionString,
			migrationsAssemblyName: null);
		return optionsBuilder.Options;
	}
}

sealed class MarkedContext(DbContextOptions<MarkedContext> options) : NorseDbContext(options)
{
	public DbSet<TemporalWidget> Widgets => Set<TemporalWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<TemporalWidget>().ToTable(TemporalEvolution.Table);
	}
}

// MarkedContext's unmarked twin: the same table name, the same columns, no ITemporalEntity. The differ
// pairs tables by name before it pairs them by entity-type name, so the only difference it can see
// between this model and MarkedContext is the marker arriving or leaving.
sealed class UnmarkedContext(DbContextOptions<UnmarkedContext> options) : NorseDbContext(options)
{
	public DbSet<PlainWidget> Widgets => Set<PlainWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<PlainWidget>().ToTable(TemporalEvolution.Table);
	}
}

// One column wider than MarkedContext, and deliberately NOT NULL: the projection rule says the history
// mirror is nullable regardless of what the main column declares (spec §3.4).
sealed class MarkedPlusContext(DbContextOptions<MarkedPlusContext> options) : NorseDbContext(options)
{
	public DbSet<TemporalWidgetPlus> Widgets => Set<TemporalWidgetPlus>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<TemporalWidgetPlus>().ToTable(TemporalEvolution.Table);
	}
}

sealed class MarkedDoublePlusContext(DbContextOptions<MarkedDoublePlusContext> options)
	: NorseDbContext(options)
{
	public DbSet<TemporalWidgetDoublePlus> Widgets => Set<TemporalWidgetDoublePlus>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<TemporalWidgetDoublePlus>().ToTable(TemporalEvolution.Table);
	}
}

// The same entity type as MarkedContext with the same property re-pointed at another column name: that
// is what makes the differ emit RenameColumn instead of a drop and an add.
sealed class MarkedRenamedColumnContext(DbContextOptions<MarkedRenamedColumnContext> options)
	: NorseDbContext(options)
{
	public DbSet<TemporalWidget> Widgets => Set<TemporalWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<TemporalWidget>().ToTable(TemporalEvolution.Table)
			.Property(widget => widget.Name).HasColumnName("label");
	}
}

sealed class MarkedWiderContext(DbContextOptions<MarkedWiderContext> options) : NorseDbContext(options)
{
	public DbSet<TemporalWidget> Widgets => Set<TemporalWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<TemporalWidget>().ToTable(TemporalEvolution.Table)
			.Property(widget => widget.Name).HasMaxLength(200);
	}
}

sealed class MarkedRenamedTableContext(DbContextOptions<MarkedRenamedTableContext> options)
	: NorseDbContext(options)
{
	public DbSet<TemporalWidget> Widgets => Set<TemporalWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<TemporalWidget>().ToTable(TemporalEvolution.RenamedTable);
	}
}

// Renamed AND one column wider. The extra column is a shadow property rather than another entity type
// on purpose: the differ pairs tables by name first and by entity-type name second, so only the same CLR
// type under two table names reads as a rename at all.
sealed class MarkedRenamedTablePlusContext(DbContextOptions<MarkedRenamedTablePlusContext> options)
	: NorseDbContext(options)
{
	public DbSet<TemporalWidget> Widgets => Set<TemporalWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		var entity = builder.Entity<TemporalWidget>();
		entity.ToTable(TemporalEvolution.RenamedTable);
		entity.Property<string>("Extra").HasMaxLength(50);
	}
}

sealed class MarkedRenamedTableMinusContext(DbContextOptions<MarkedRenamedTableMinusContext> options)
	: NorseDbContext(options)
{
	public DbSet<TemporalWidget> Widgets => Set<TemporalWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		var entity = builder.Entity<TemporalWidget>();
		entity.ToTable(TemporalEvolution.RenamedTable);
		entity.Ignore(widget => widget.Name);
	}
}

// Renamed, one column gone and one column arrived: the batch that puts column operations on both sides
// of the rename.
sealed class MarkedRenamedTableSwappedColumnContext(
	DbContextOptions<MarkedRenamedTableSwappedColumnContext> options) : NorseDbContext(options)
{
	public DbSet<TemporalWidget> Widgets => Set<TemporalWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		var entity = builder.Entity<TemporalWidget>();
		entity.ToTable(TemporalEvolution.RenamedTable);
		entity.Ignore(widget => widget.Name);
		entity.Property<string>("Extra").HasMaxLength(50);
	}
}

// No entities at all: the target model an entity drop leaves behind. Whatever the drop operation carries
// is the whole record of the table's temporality, because the model holds nothing.
sealed class EmptyContext(DbContextOptions<EmptyContext> options) : NorseDbContext(options)
{
	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
	}
}

sealed class MarkedOtherKeyContext(DbContextOptions<MarkedOtherKeyContext> options)
	: NorseDbContext(options)
{
	public DbSet<TemporalWidget> Widgets => Set<TemporalWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<TemporalWidget>().ToTable(TemporalEvolution.Table)
			.HasKey(widget => widget.Name);
	}
}

sealed class MarkedOtherSchemaContext(DbContextOptions<MarkedOtherSchemaContext> options)
	: NorseDbContext(options)
{
	public DbSet<TemporalWidget> Widgets => Set<TemporalWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<TemporalWidget>().ToTable(TemporalEvolution.Table, TemporalEvolution.OtherSchema);
	}
}

// The unmarked mirror image of the set above: every evolution shape has to pass through to Npgsql's own
// SQL untouched when the table is not temporal.
sealed class PlainContext(DbContextOptions<PlainContext> options) : NorseDbContext(options)
{
	public DbSet<PlainWidget> Widgets => Set<PlainWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<PlainWidget>().ToTable(TemporalEvolution.PlainTable);
	}
}

sealed class PlainPlusContext(DbContextOptions<PlainPlusContext> options) : NorseDbContext(options)
{
	public DbSet<PlainWidgetPlus> Widgets => Set<PlainWidgetPlus>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<PlainWidgetPlus>().ToTable(TemporalEvolution.PlainTable);
	}
}

sealed class PlainRenamedTableContext(DbContextOptions<PlainRenamedTableContext> options)
	: NorseDbContext(options)
{
	public DbSet<PlainWidget> Widgets => Set<PlainWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<PlainWidget>().ToTable("renamed_plain_widgets");
	}
}

sealed class PlainOtherKeyContext(DbContextOptions<PlainOtherKeyContext> options)
	: NorseDbContext(options)
{
	public DbSet<PlainWidget> Widgets => Set<PlainWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<PlainWidget>().ToTable(TemporalEvolution.PlainTable)
			.HasKey(widget => widget.Name);
	}
}

sealed record TemporalWidget : ITemporalEntity, INorseEntity<TemporalWidget>
{
	public int Id { get; init; }

	[MaxLength(100)]
	public string Name { get; init; } = "";

	public static void Configure(EntityTypeBuilder<TemporalWidget> builder) { }
}

sealed record TemporalWidgetPlus : ITemporalEntity, INorseEntity<TemporalWidgetPlus>
{
	public int Id { get; init; }

	[MaxLength(100)]
	public string Name { get; init; } = "";

	[MaxLength(50)]
	public string Extra { get; init; } = "";

	public static void Configure(EntityTypeBuilder<TemporalWidgetPlus> builder) { }
}

sealed record TemporalWidgetDoublePlus : ITemporalEntity, INorseEntity<TemporalWidgetDoublePlus>
{
	public int Id { get; init; }

	[MaxLength(100)]
	public string Name { get; init; } = "";

	[MaxLength(50)]
	public string Extra { get; init; } = "";

	[MaxLength(50)]
	public string Note { get; init; } = "";

	public static void Configure(EntityTypeBuilder<TemporalWidgetDoublePlus> builder) { }
}

sealed record PlainWidget : INorseEntity<PlainWidget>
{
	public int Id { get; init; }

	[MaxLength(100)]
	public string Name { get; init; } = "";

	public static void Configure(EntityTypeBuilder<PlainWidget> builder) { }
}

/// <summary>
/// The temporal entity with a split fragment — <c>AccessCount</c> lives in a second table the apparatus
/// deliberately never touches (spec §2.3). Shared rather than duplicated: the create-path snapshot suite
/// asserts the fragment table gets no apparatus, and the integration suite proves the same thing against
/// a real server by updating only the fragment and finding no history row.
/// </summary>
sealed record SplitTemporalWidget : ITemporalEntity, INorseEntity<SplitTemporalWidget>
{
	public int Id { get; init; }

	[MaxLength(100)]
	public string Name { get; init; } = "";

	public int AccessCount { get; init; }

	public static void Configure(EntityTypeBuilder<SplitTemporalWidget> builder) { }
}

sealed record PlainWidgetPlus : INorseEntity<PlainWidgetPlus>
{
	public int Id { get; init; }

	[MaxLength(100)]
	public string Name { get; init; } = "";

	[MaxLength(50)]
	public string Extra { get; init; } = "";

	public static void Configure(EntityTypeBuilder<PlainWidgetPlus> builder) { }
}
