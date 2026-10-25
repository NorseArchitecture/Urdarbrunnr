using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Norse.Persistence.EntityFramework.PostgreSQL.Tests;

/// <summary>
///     The model variants for stored generated columns on temporal tables (chassis spec §3.2 and §3.4, both
///     amended 2026-10-24 by identity PoC G6) and for the chassis-owned <c>norse_utf8_sha256</c> function
///     (identity split spec §4.6, r9). Shared by the snapshot suite and the live suite, so the SQL one asserts
///     on is the SQL the other applies.
/// </summary>
static class TemporalGeneratedColumns
{
	public const string HashedTable = "hashed_widgets";

	public const string PlainHashedTable = "plain_hashed_widgets";

	/// <summary>The generation expression the identity realm's <c>claim_value_hash</c> uses, over <c>name</c>.</summary>
	public const string HashSql = "norse_utf8_sha256(name)";

	/// <summary>The chassis-owned function, verbatim from spec §4.6 (r9).</summary>
	public const string FunctionSql =
		"CREATE OR REPLACE FUNCTION norse_utf8_sha256(text) RETURNS bytea LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE RETURN pg_catalog.sha256(pg_catalog.convert_to($1, 'UTF8'));";

	/// <summary>A generated column is added to an existing temporal table: <c>AddColumn</c>.</summary>
	public static string AddGeneratedColumnSql()
	{
		using MarkedContext from = new(TemporalEvolution.Options<MarkedContext>());
		using MarkedHashedContext to = new(TemporalEvolution.Options<MarkedHashedContext>());
		return TemporalEvolution.TransitionSql(from, to);
	}

	/// <summary>A plain <c>bytea</c> column on a temporal table becomes generated: <c>AlterColumn</c>.</summary>
	public static string AlterToGeneratedColumnSql()
	{
		using MarkedRawHashContext from = new(TemporalEvolution.Options<MarkedRawHashContext>());
		using MarkedHashedContext to = new(TemporalEvolution.Options<MarkedHashedContext>());
		return TemporalEvolution.TransitionSql(from, to);
	}

	/// <summary>Temporality arrives on a table that already carries a generated column: the enable transition.</summary>
	public static string EnableWithGeneratedColumnSql()
	{
		using UnmarkedHashedContext from = new(TemporalEvolution.Options<UnmarkedHashedContext>());
		using MarkedHashedContext to = new(TemporalEvolution.Options<MarkedHashedContext>());
		return TemporalEvolution.TransitionSql(from, to);
	}
}

/// <summary>A temporal table created with a stored generated column naming <c>norse_utf8_sha256</c>.</summary>
sealed class HashedWidgetContext(DbContextOptions<HashedWidgetContext> options) : NorseDbContext(options)
{
	public DbSet<HashedWidget> Widgets => Set<HashedWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		HashedWidget.Configure(builder.Entity<HashedWidget>().ToTable(TemporalGeneratedColumns.HashedTable));
	}
}

/// <summary>The function is not a temporal concern: an unmarked table naming it needs it just as much.</summary>
sealed class PlainHashedWidgetContext(DbContextOptions<PlainHashedWidgetContext> options)
	: NorseDbContext(options)
{
	public DbSet<PlainHashedWidget> Widgets => Set<PlainHashedWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		PlainHashedWidget.Configure(builder.Entity<PlainHashedWidget>().ToTable(TemporalGeneratedColumns.PlainHashedTable));
	}
}

/// <summary>Two tables naming the function in one batch: it is emitted once, ahead of the first.</summary>
sealed class TwoHashedWidgetContext(DbContextOptions<TwoHashedWidgetContext> options) : NorseDbContext(options)
{
	public DbSet<HashedWidget> Widgets => Set<HashedWidget>();

	public DbSet<PlainHashedWidget> PlainWidgets => Set<PlainHashedWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		HashedWidget.Configure(builder.Entity<HashedWidget>().ToTable(TemporalGeneratedColumns.HashedTable));
		PlainHashedWidget.Configure(builder.Entity<PlainHashedWidget>().ToTable(TemporalGeneratedColumns.PlainHashedTable));
	}
}

/// <summary>A temporal table whose stored generated column does not name the function: no function.</summary>
sealed class LoweredWidgetContext(DbContextOptions<LoweredWidgetContext> options) : NorseDbContext(options)
{
	public DbSet<LoweredWidget> Widgets => Set<LoweredWidget>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		LoweredWidget.Configure(builder.Entity<LoweredWidget>().ToTable("lowered_widgets"));
	}
}

/// <summary><see cref="MarkedContext" /> plus a generated hash: the add-column target.</summary>
sealed class MarkedHashedContext(DbContextOptions<MarkedHashedContext> options) : NorseDbContext(options)
{
	public DbSet<TemporalWidgetHashed> Widgets => Set<TemporalWidgetHashed>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		TemporalWidgetHashed.Configure(builder.Entity<TemporalWidgetHashed>().ToTable(TemporalEvolution.Table));
	}
}

/// <summary>The same column as <see cref="MarkedHashedContext" />, but plain: the alter-column source.</summary>
sealed class MarkedRawHashContext(DbContextOptions<MarkedRawHashContext> options) : NorseDbContext(options)
{
	public DbSet<TemporalWidgetRawHash> Widgets => Set<TemporalWidgetRawHash>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		builder.Entity<TemporalWidgetRawHash>().ToTable(TemporalEvolution.Table);
	}
}

/// <summary><see cref="MarkedHashedContext" />'s unmarked twin: the enable-transition source.</summary>
sealed class UnmarkedHashedContext(DbContextOptions<UnmarkedHashedContext> options) : NorseDbContext(options)
{
	public DbSet<PlainWidgetHashed> Widgets => Set<PlainWidgetHashed>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);
		builder.HasDefaultSchema(TemporalEvolution.Schema);
		PlainWidgetHashed.Configure(builder.Entity<PlainWidgetHashed>().ToTable(TemporalEvolution.Table));
	}
}

sealed record HashedWidget : ITemporalEntity, INorseEntity<HashedWidget>
{
	public int Id { get; init; }

	[MaxLength(100)] public string Name { get; init; } = "";

	[MaxLength(32)] public byte[] NameHash { get; init; } = [];

	public static void Configure(EntityTypeBuilder<HashedWidget> builder) =>
		builder.Property(widget => widget.NameHash).HasComputedColumnSql(TemporalGeneratedColumns.HashSql, stored: true);
}

sealed record PlainHashedWidget : INorseEntity<PlainHashedWidget>
{
	public int Id { get; init; }

	[MaxLength(100)] public string Name { get; init; } = "";

	[MaxLength(32)] public byte[] NameHash { get; init; } = [];

	public static void Configure(EntityTypeBuilder<PlainHashedWidget> builder) =>
		builder.Property(widget => widget.NameHash).HasComputedColumnSql(TemporalGeneratedColumns.HashSql, stored: true);
}

sealed record LoweredWidget : ITemporalEntity, INorseEntity<LoweredWidget>
{
	public int Id { get; init; }

	[MaxLength(100)] public string Name { get; init; } = "";

	[MaxLength(100)] public string NameLower { get; init; } = "";

	public static void Configure(EntityTypeBuilder<LoweredWidget> builder) =>
		builder.Property(widget => widget.NameLower).HasComputedColumnSql("lower(name)", stored: true);
}

sealed record TemporalWidgetHashed : ITemporalEntity, INorseEntity<TemporalWidgetHashed>
{
	public int Id { get; init; }

	[MaxLength(100)] public string Name { get; init; } = "";

	[MaxLength(32)] public byte[] NameHash { get; init; } = [];

	public static void Configure(EntityTypeBuilder<TemporalWidgetHashed> builder) =>
		builder.Property(widget => widget.NameHash).HasComputedColumnSql(TemporalGeneratedColumns.HashSql, stored: true);
}

sealed record TemporalWidgetRawHash : ITemporalEntity, INorseEntity<TemporalWidgetRawHash>
{
	public int Id { get; init; }

	[MaxLength(100)] public string Name { get; init; } = "";

	[MaxLength(32)] public byte[] NameHash { get; init; } = [];

	public static void Configure(EntityTypeBuilder<TemporalWidgetRawHash> builder)
	{
	}
}

sealed record PlainWidgetHashed : INorseEntity<PlainWidgetHashed>
{
	public int Id { get; init; }

	[MaxLength(100)] public string Name { get; init; } = "";

	[MaxLength(32)] public byte[] NameHash { get; init; } = [];

	public static void Configure(EntityTypeBuilder<PlainWidgetHashed> builder) =>
		builder.Property(widget => widget.NameHash).HasComputedColumnSql(TemporalGeneratedColumns.HashSql, stored: true);
}
