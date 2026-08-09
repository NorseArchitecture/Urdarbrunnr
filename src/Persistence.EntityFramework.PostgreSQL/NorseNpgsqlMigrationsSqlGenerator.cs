using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace Norse.Persistence.EntityFramework.PostgreSQL;

// EF1001: INpgsqlSingletonOptions is EF-internal by attribute, and is the second constructor
// parameter NpgsqlMigrationsSqlGenerator requires. Forwarded untouched.
#pragma warning disable EF1001

/// <summary>
///     Emits the PostgreSQL temporal apparatus around Npgsql's own migration SQL. The emission-seam spike
///     (<c>../Glitnir/poc/ef-temporal-emission/FINDINGS.md</c>) established two seams; a third was measured
///     afterwards for the one shape the spike did not scaffold, and all three are used here. Ordinary
///     operations do not carry the <see cref="NorseAnnotationNames.Temporal" /> marker, so they identify their
///     table by consulting the target model. The marker transitions of §3.3 do carry it — they exist as
///     operations at all only because <see cref="NorseNpgsqlAnnotationProvider" /> projects the marker onto
///     the relational table — and on the disable side the target model still holds the table but no longer
///     marks it, so <see cref="AlterTableOperation.OldTable" /> is the only record that it ever was temporal.
///     Dropping the entity reaches neither: the table leaves the target model entirely, and EF builds
///     <see cref="DropTableOperation" /> from <c>IMigrationsAnnotationProvider.ForRemove</c> instead of
///     copying the model's annotations, which is why <see cref="NorseNpgsqlMigrationsAnnotationProvider" />
///     exists. A marker transition combined with a column change or a table rename on the same table is
///     rejected by name (see <c>GuardCombinedTransitions</c>), as are the two evolutions §3.4 refuses outright (see
///     <c>GuardRejectedOperations</c>). Everything else evolves in the fixed drop-view-first order of
///     ruling 16.
/// </summary>
/// <param name="dependencies">The migrations SQL generator dependencies.</param>
/// <param name="npgsqlOptions">Npgsql's singleton options, forwarded to the base generator.</param>
sealed class NorseNpgsqlMigrationsSqlGenerator(
	MigrationsSqlGeneratorDependencies dependencies,
	INpgsqlSingletonOptions npgsqlOptions) : NpgsqlMigrationsSqlGenerator(dependencies, npgsqlOptions)
{
	// PostgreSQL's own default schema, used only when neither the operation nor the model named one.
	// The SECURITY DEFINER function pins its search path to pg_catalog, so every apparatus object has
	// to be qualified with a real name — but Npgsql emits the main table unqualified, letting the
	// session search path resolve it. Rather than assume those agree, the migration asserts it: see
	// _assertDefaultSchemaIsPublic.
	const string DefaultSchema = "public";

	// Per-batch, same discipline: how many column operations each temporal table still has coming, whether
	// its timeline view has already been dropped, and whether its rename is still ahead. Keyed by the
	// table's TARGET name, never by the name an individual operation carries — EF sorts a dropped column
	// ahead of its table's rename (old name) and an added one after it (new name), and those are one
	// table's evolution, not two. See EndColumnEvolution for why the count is load-bearing.
	readonly Dictionary<(string? Schema, string Table), ColumnEvolution> _columnEvolutions = [];

	// Per-batch: where this batch's renames send each table. A column operation that EF sorted ahead of
	// its table's rename still carries the OLD name, which the target model has never heard of — so its
	// table is looked up through this map, while its SQL is still emitted against the name the table
	// actually has at that point in the batch.
	readonly Dictionary<(string? Schema, string Table), (string? Schema, string Table)> _renames = [];
	bool _assertDefaultSchemaIsPublic;

	// The prelude (§3.1 steps 0-1) is per-migration, not per-table. These flags are reset at the top
	// of every batch rather than trusted to the generator's service lifetime.
	bool _preludeEmitted;

	/// <inheritdoc />
	public override IReadOnlyList<MigrationCommand> Generate(IReadOnlyList<MigrationOperation> operations,
		IModel? model = null, MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
	{
		_preludeEmitted = false;
		// The scan first: both guards below read the batch's rename map, and so does every column
		// operation that EF sorted ahead of its own table's rename.
		ScanBatch(operations, model);
		GuardCombinedTransitions(operations);
		GuardRejectedOperations(operations, model);
		// Decided across the whole batch, not per table: the prelude is emitted once, at the first
		// temporal table, and has to carry the guard even when it is a later table that falls through.
		_assertDefaultSchemaIsPublic = model?.GetDefaultSchema() is null
			&& operations.Any(operation => operation switch
			{
				CreateTableOperation create => create.Schema is null && IsTemporal(model, create.Name, create.Schema),
				AlterTableOperation alter => alter.Schema is null && IsEnableTransition(alter),
				_ => false
			});
		return base.Generate(operations, model, options);
	}

	/// <inheritdoc />
	protected override void Generate(CreateTableOperation operation, IModel? model,
		MigrationCommandListBuilder builder, bool terminate = true)
	{
		if (!IsTemporal(model, operation.Name, operation.Schema))
		{
			base.Generate(operation, model, builder, terminate);
			return;
		}

		var schema = SchemaOf(operation.Schema, model);
		var table = operation.Name;
		List<(string Name, string StoreType, bool IsNullable)> columns =
			[.. operation.Columns.Select(column => (column.Name, StoreType: StoreTypeOf(column), column.IsNullable))];
		var pkColumns = operation.PrimaryKey?.Columns ?? throw new InvalidOperationException(
			$"Temporal table '{table}' has no primary key; the WITHOUT OVERLAPS history key requires one.");

		// Before base.Generate, not after — see AppendPrelude.
		AppendPrelude(builder);
		base.Generate(operation, model, builder, terminate);
		AppendBlock(builder, TemporalSqlEmitter.SystemPeriodColumn(schema, table));
		AppendBlock(builder, TemporalSqlEmitter.HistoryTable(schema, table, columns, pkColumns));
		AppendBlock(builder, TemporalSqlEmitter.TriggerFunction(schema, table, columns, orReplace: false));
		AppendBlock(builder, TemporalSqlEmitter.Triggers(schema, table));
		AppendBlock(builder, TemporalSqlEmitter.TimelineView(schema, table, columns));
	}

	/// <inheritdoc />
	/// <remarks>
	///     The marker transitions of §3.3. Unlike the column operations, these carry the marker themselves:
	///     the differ produces this operation only because <see cref="NorseNpgsqlAnnotationProvider" /> projects
	///     <see cref="NorseAnnotationNames.Temporal" /> onto the table. On the disable side the target model
	///     still holds the table — unmarked — so nothing on the target side records that it used to be
	///     temporal, and <see cref="AlterTableOperation.OldTable" /> is the only available discriminator.
	///     An alteration
	///     that leaves the marker as it found it — a comment or storage-parameter change on a table that is
	///     temporal both before and after — is no transition and gets nothing beyond the base emission.
	/// </remarks>
	protected override void Generate(AlterTableOperation operation, IModel? model,
		MigrationCommandListBuilder builder)
	{
		base.Generate(operation, model, builder);
		var isTemporal = IsMarkedTemporal(operation);
		if (isTemporal == IsMarkedTemporal(operation.OldTable))
			return;

		var schema = SchemaOf(operation.Schema, model);
		var table = operation.Name;
		if (!isTemporal)
		{
			AppendBlock(builder, TemporalSqlEmitter.DisableTransition(schema, table));
			return;
		}

		// The operation carries no columns, so the history mirror is projected from the target model's
		// table — which is where the marker just arrived, so it is guaranteed to be there.
		var (columns, pkColumns) = TargetTableShape(model, table, operation.Schema);
		AppendPrelude(builder);
		AppendBlock(builder, TemporalSqlEmitter.EnableTransition(schema, table, columns, pkColumns));
	}

	/// <inheritdoc />
	/// <remarks>
	///     Dropping the entity (§3.4), the one shape whose marker reaches neither seam the spike named. The
	///     target model has no such table to consult, and the differ builds this operation from
	///     <c>IMigrationsAnnotationProvider.ForRemove(ITable)</c> rather than copying the model's table
	///     annotations — so the marker arrives only because
	///     <see cref="NorseNpgsqlMigrationsAnnotationProvider" /> forwards it there. The apparatus is torn down
	///     ahead of the base <c>DROP TABLE</c>, view first as everywhere else: PostgreSQL refuses to drop a
	///     table under a dependent view (2BP01), and the versioning function is not owned by the table and
	///     would otherwise outlive it. Recorded history dies with the entity — the same visible destruction as
	///     the disable transition (§3.3).
	/// </remarks>
	protected override void Generate(DropTableOperation operation, IModel? model,
		MigrationCommandListBuilder builder, bool terminate = true)
	{
		if (IsMarkedTemporal(operation))
		{
			var schema = SchemaOf(operation.Schema, model);
			AppendBlock(builder, TemporalSqlEmitter.DropTimelineView(schema, operation.Name));
			AppendBlock(builder,
				TemporalSqlEmitter.DropTriggersAndFunction(schema, operation.Name, operation.Name));
			AppendBlock(builder, TemporalSqlEmitter.DropHistoryTable(schema, operation.Name));
		}

		base.Generate(operation, model, builder, terminate);
	}

	/// <inheritdoc />
	/// <remarks>
	///     Evolution (§3.4). The added column is mirrored onto history by the projection rule — name and
	///     store type only, nullable regardless of what the main column declares, and never its default.
	/// </remarks>
	protected override void Generate(AddColumnOperation operation, IModel? model,
		MigrationCommandListBuilder builder, bool terminate = true)
	{
		if (!IsTemporalInBatch(model, operation.Table, operation.Schema))
		{
			base.Generate(operation, model, builder, terminate);
			return;
		}

		var schema = SchemaOf(operation.Schema, model);
		BeginColumnEvolution(builder, schema, operation.Table, operation.Schema);
		base.Generate(operation, model, builder, terminate);
		AppendBlock(builder, TemporalSqlEmitter.AddHistoryColumn(schema, operation.Table, operation.Name,
			StoreTypeOf(operation)));
		EndColumnEvolution(builder, model, schema, operation.Table, operation.Schema);
	}

	/// <inheritdoc />
	/// <remarks>
	///     Evolution (§3.4): history is a version log, not an archive of dead columns, so the drop mirrors and
	///     the column's recorded values go with it.
	/// </remarks>
	protected override void Generate(DropColumnOperation operation, IModel? model,
		MigrationCommandListBuilder builder, bool terminate = true)
	{
		if (!IsTemporalInBatch(model, operation.Table, operation.Schema))
		{
			base.Generate(operation, model, builder, terminate);
			return;
		}

		var schema = SchemaOf(operation.Schema, model);
		BeginColumnEvolution(builder, schema, operation.Table, operation.Schema);
		base.Generate(operation, model, builder, terminate);
		AppendBlock(builder, TemporalSqlEmitter.DropHistoryColumn(schema, operation.Table, operation.Name));
		EndColumnEvolution(builder, model, schema, operation.Table, operation.Schema);
	}

	/// <inheritdoc />
	/// <remarks>Evolution (§3.4): renamed on history too, never dropped and re-added.</remarks>
	protected override void Generate(RenameColumnOperation operation, IModel? model,
		MigrationCommandListBuilder builder)
	{
		if (!IsTemporalInBatch(model, operation.Table, operation.Schema))
		{
			base.Generate(operation, model, builder);
			return;
		}

		var schema = SchemaOf(operation.Schema, model);
		BeginColumnEvolution(builder, schema, operation.Table, operation.Schema);
		base.Generate(operation, model, builder);
		AppendBlock(builder, TemporalSqlEmitter.RenameHistoryColumn(schema, operation.Table, operation.Name,
			operation.NewName));
		EndColumnEvolution(builder, model, schema, operation.Table, operation.Schema);
	}

	/// <inheritdoc />
	/// <remarks>
	///     Evolution (§3.4): the store type mirrors onto history, nothing else does. A history column is
	///     nullable whatever the main column declares, so a nullability change has no mirror at all, and the
	///     temporal key components took their <c>NOT NULL</c> the day the history table was created.
	/// </remarks>
	protected override void Generate(AlterColumnOperation operation, IModel? model,
		MigrationCommandListBuilder builder)
	{
		if (!IsTemporalInBatch(model, operation.Table, operation.Schema))
		{
			base.Generate(operation, model, builder);
			return;
		}

		var schema = SchemaOf(operation.Schema, model);
		BeginColumnEvolution(builder, schema, operation.Table, operation.Schema);
		base.Generate(operation, model, builder);
		AppendBlock(builder, TemporalSqlEmitter.AlterHistoryColumnType(schema, operation.Table, operation.Name,
			StoreTypeOf(operation)));
		EndColumnEvolution(builder, model, schema, operation.Table, operation.Schema);
	}

	/// <inheritdoc />
	/// <remarks>
	///     The rename choreography of §3.4, and the one evolution shape that is not the five-statement column
	///     order. PostgreSQL keeps a renamed table's triggers — under their old names, still bound to the old
	///     function — so creating a newly named function would rebind nothing and the table would go on
	///     versioning through stale apparatus. Only the two tables rename in place, preserving the history
	///     data mapping; every other apparatus object is retired by name and recreated. A rename that also
	///     moves schemas never reaches here: <c>GuardRejectedOperations</c> refused the batch.
	/// </remarks>
	/// <remarks>
	///     When the same migration also changes the renamed table's columns, the rename is one step inside
	///     that table's single column-evolution group rather than a self-contained batch of its own. EF sorts
	///     a dropped column ahead of the rename and an added one after it, so the group can straddle the
	///     rename: whichever side comes first drops the view (under the name the table has at that moment),
	///     and whichever operation is last recreates it from the finished shape. This method therefore drops
	///     the old view only if nothing already has, and creates the new one only when no column operation
	///     remains to do it. The trigger function is always created here regardless, because the triggers it
	///     binds need it and PostgreSQL resolves a plpgsql body's column references at first execution rather
	///     than at creation; a later column operation replaces it in place over the finished shape. Every case
	///     here was proven against a live server, not reasoned about.
	/// </remarks>
	protected override void Generate(RenameTableOperation operation, IModel? model,
		MigrationCommandListBuilder builder)
	{
		var newTable = operation.NewName ?? operation.Name;
		var newSchema = operation.NewSchema ?? operation.Schema;
		if (!IsTemporal(model, newTable, newSchema))
		{
			base.Generate(operation, model, builder);
			return;
		}

		var schema = SchemaOf(newSchema, model);
		var (columns, _) = TargetTableShape(model, newTable, newSchema);
		var key = (newSchema, newTable);
		var grouped = _columnEvolutions.TryGetValue(key, out var group);
		// A column operation the batch sorted ahead of this rename has already dropped the old view.
		if (!grouped || !group.ViewDropped)
			AppendBlock(builder, TemporalSqlEmitter.DropTimelineView(schema, operation.Name));
		base.Generate(operation, model, builder);
		AppendBlock(builder, TemporalSqlEmitter.RenameHistoryTable(schema, operation.Name, newTable));
		AppendBlock(builder, TemporalSqlEmitter.DropTriggersAndFunction(schema, newTable, operation.Name));
		AppendBlock(builder, TemporalSqlEmitter.TriggerFunction(schema, newTable, columns, orReplace: false));
		AppendBlock(builder, TemporalSqlEmitter.Triggers(schema, newTable));
		if (!grouped)
		{
			AppendBlock(builder, TemporalSqlEmitter.TimelineView(schema, newTable, columns));
			return;
		}

		// From here the rename is behind the table, so whatever column operations remain own the recreate.
		var handOff = group.Remaining > 0;
		_columnEvolutions[key] = group with { ViewDropped = handOff, RenamePending = false };
		if (handOff)
			return;

		AppendBlock(builder, TemporalSqlEmitter.TimelineView(schema, newTable, columns));
	}

	// Evolution step 1: the timeline view goes first, once per table per batch — dropped under the name the
	// table carries at this point in the batch, which is the pre-rename name for an operation EF sorted
	// ahead of its table's rename.
	void BeginColumnEvolution(MigrationCommandListBuilder builder, string schema, string table,
		string? operationSchema)
	{
		var key = TargetNameOf(operationSchema, table);
		var state = _columnEvolutions[key];
		if (state.ViewDropped)
			return;
		AppendBlock(builder, TemporalSqlEmitter.DropTimelineView(schema, table));
		_columnEvolutions[key] = state with { ViewDropped = true };
	}

	// Evolution steps 4-5, once the table's last column operation in this batch has been emitted. Both
	// are projected from the TARGET column shape, which only exists in full after every one of that
	// table's operations has run: recreating the view after the first of two added columns would select a
	// column the second add has not made yet, and PostgreSQL would refuse the migration mid-flight. A
	// single-operation batch — the common case — is unaffected: the five statements land in the fixed
	// order of ruling 16, exactly as though each operation carried its own drop and recreate.
	//
	// A table whose rename is still ahead of this operation owns no recreate either, whatever the count
	// says: anything built here would carry the pre-rename name, and the rename choreography retires it
	// moments later. The rename hands the view on to whatever follows it, or builds it itself when nothing
	// does.
	void EndColumnEvolution(MigrationCommandListBuilder builder, IModel? model, string schema, string table,
		string? operationSchema)
	{
		var key = TargetNameOf(operationSchema, table);
		var state = _columnEvolutions[key] with { Remaining = _columnEvolutions[key].Remaining - 1 };
		_columnEvolutions[key] = state;
		if (state.Remaining > 0 || state.RenamePending)
			return;

		var (columns, _) = TargetTableShape(model, key.Table, key.Schema);
		AppendBlock(builder, TemporalSqlEmitter.TriggerFunction(schema, table, columns, orReplace: true));
		AppendBlock(builder, TemporalSqlEmitter.TimelineView(schema, table, columns));
	}

	// Records where the batch's renames send each table, then counts each temporal table's column
	// operations before any of them is emitted, so the last one can recognize itself. Cleared at the top
	// of every batch, like the prelude flags.
	void ScanBatch(IReadOnlyList<MigrationOperation> operations, IModel? model)
	{
		_renames.Clear();
		foreach (var rename in operations.OfType<RenameTableOperation>())
			_renames[(rename.Schema, rename.Name)] =
				(rename.NewSchema ?? rename.Schema, rename.NewName ?? rename.Name);

		_columnEvolutions.Clear();
		foreach (var operation in operations)
		{
			if (ColumnOperationTarget(operation) is not { } target
				|| !IsTemporalInBatch(model, target.Table, target.Schema))
				continue;
			var key = TargetNameOf(target.Schema, target.Table);
			_columnEvolutions[key] = _columnEvolutions.TryGetValue(key, out var state) ?
				state with { Remaining = state.Remaining + 1 } :
				new ColumnEvolution(Remaining: 1, ViewDropped: false,
					RenamePending: _renames.ContainsValue(key));
		}
	}

	// Temporality as of this batch: the table under the name the operation carries, or — when the batch
	// renames that table and EF sorted this operation ahead of the rename — the table it is about to
	// become, which is the only one the target model knows.
	bool IsTemporalInBatch(IModel? model, string table, string? schema)
	{
		var (targetSchema, targetTable) = TargetNameOf(schema, table);
		return IsTemporal(model, targetTable, targetSchema);
	}

	(string? Schema, string Table) TargetNameOf(string? schema, string table) =>
		_renames.TryGetValue((schema, table), out var renamed) ?
			renamed :
			(schema, table);

	// The evolutions §3.4 refuses outright, caught across the whole batch before a line of SQL is emitted
	// so the diagnostic never depends on where EF happened to sort the operation. A primary-key change
	// would leave the history table's WITHOUT OVERLAPS key describing the wrong columns; a schema move
	// would strand the apparatus in the schema it was built in. Both would be silently-wrong history
	// constraints, which is the one outcome this design will not ship.
	void GuardRejectedOperations(IReadOnlyList<MigrationOperation> operations, IModel? model)
	{
		foreach (var operation in operations)
		{
			switch (operation)
			{
				case AddPrimaryKeyOperation add when !IsRenamedInBatch(add.Schema, add.Table)
					&& IsTemporal(model, add.Table, add.Schema):
					throw RejectedEvolution("AddPrimaryKey", add.Table);
				case DropPrimaryKeyOperation drop when !IsRenamedInBatch(drop.Schema, drop.Table)
					&& IsTemporal(model, drop.Table, drop.Schema):
					throw RejectedEvolution("DropPrimaryKey", drop.Table);
				case RenameTableOperation rename
					when !string.Equals(rename.Schema, rename.NewSchema, StringComparison.Ordinal)
					&& IsTemporal(model, rename.NewName ?? rename.Name, rename.NewSchema):
					throw RejectedEvolution(
						$"A schema move to '{rename.NewSchema ?? DefaultSchema}'", rename.Name);
				default:
					continue;
			}
		}
	}

	// A table rename renames its primary-key CONSTRAINT too, and EF has no rename-constraint operation, so
	// it expresses that as a drop and an add over unchanged columns — collateral of the rename, not a key
	// change, and the history key stays correct. Those are exempt. (The residual: a migration that renames
	// the table AND changes its key in one step is indistinguishable from a plain rename in the operations
	// EF emits — the source key is not in the target model — and passes as a rename. Recorded rather than
	// papered over; the sanctioned path for a key change is its own migration either way.)
	bool IsRenamedInBatch(string? schema, string table) =>
		_renames.ContainsKey((schema, table)) || _renames.ContainsValue((schema, table));

	static InvalidOperationException RejectedEvolution(string change, string table) =>
		new($"{change} on temporal table '{table}' is not supported. The history table's WITHOUT OVERLAPS "
			+ "primary key and every derived apparatus name are built from the table's current key and "
			+ "name, and neither can be rewritten in place without leaving the history constraints wrong. "
			+ "The sanctioned path is deliberate: drop temporality first (remove ITemporalEntity — visible "
			+ "destruction), perform the change, re-mark; or author the migration by hand for the rare "
			+ "case that must preserve history across the change.");

	// Steps 0-1, at most once per batch. Both call sites share the flag, so a migration that creates one
	// temporal table and enables temporality on another still asserts the floor and the extension once.
	// The evolution paths deliberately do not call this: they change an apparatus that already exists,
	// which means the floor was asserted and btree_gist was resolved by the migration that built it, and
	// a mirror statement aimed at the wrong schema fails on its own missing relation rather than landing
	// somewhere plausible — the silent-success risk the create path's schema assert exists to close does
	// not arise here.
	//
	// Both call sites emit this BEFORE the operation it guards, never after. The create path calls this
	// ahead of base.Generate(CreateTableOperation…): in a non-transactional script workflow
	// (GenerateCreateScript, psql without a wrapping transaction) the floor/schema asserts have to run
	// before Npgsql's unqualified main table lands, or a wrong search_path can create it in the wrong
	// schema before the assert that exists to catch exactly that ever fires. The enable-transition path's
	// base.Generate(AlterTableOperation…) call runs before AppendPrelude, so it may alter the unqualified
	// table first (a comment or storage-parameter change) — but a statement aimed at the wrong schema
	// there fails loudly on a missing relation rather than landing silently, the same risk posture as the
	// mirror-statement case above, without needing the ordering guarantee the create path relies on.
	void AppendPrelude(MigrationCommandListBuilder builder)
	{
		if (_preludeEmitted)
			return;
		AppendBlock(builder, TemporalSqlEmitter.FloorAssert(_assertDefaultSchemaIsPublic));
		AppendBlock(builder, TemporalSqlEmitter.BtreeGistGuard());
		_preludeEmitted = true;
	}

	// A marker transition rebuilds the whole apparatus from the target model's column shape, but EF orders
	// the AlterTableOperation and the batch's column operations independently of that shape, and whichever
	// way it orders them one direction is wrong. Enable plus an added property: CREATE VIEW selects a
	// column the main table has not been given yet. Disable plus a dropped one: the differ sorts DROP
	// COLUMN ahead of the alteration, so it runs while {table}_timeline still selects that column. Either
	// way PostgreSQL refuses mid-migration, so the migration fails here, by name, instead — the same
	// posture the design takes on primary-key changes and schema moves (§3.4). The evolution handling
	// below does not relax it: its per-table grouping orders a table's COLUMN operations against each
	// other, and the marker transition is not one of them — EF is free to sort the AlterTableOperation to
	// either side of them, so one of the two directions stays wrong however the columns are grouped.
	//
	// A rename collides the same way, and was measured to be worse: the transition operation carries the
	// table's TARGET name (Generate(RenameTableOperation…) keys temporality off the target model too), so
	// an unguarded enable-plus-rename fires the full rename choreography — DROP VIEW, history-table
	// rename, trigger retirement — against apparatus that was never built under the old name, and an
	// unguarded disable-plus-rename strands teardown against a name the apparatus was never renamed into.
	// Detecting it needs the batch's rename map (_renames, populated by ScanBatch before either guard
	// runs), which is why this guard reads instance state instead of staying static.
	void GuardCombinedTransitions(IReadOnlyList<MigrationOperation> operations)
	{
		foreach (var transition in operations.OfType<AlterTableOperation>()
			.Where(operation => IsMarkedTemporal(operation) != IsMarkedTemporal(operation.OldTable)))
		{
			var columnCollision = operations
				.Select(ColumnOperationTarget)
				.FirstOrDefault(target => target?.Table == transition.Name && target?.Schema == transition.Schema);
			if (columnCollision is { } column)
				throw new InvalidOperationException(
					$"Temporality is being {(IsMarkedTemporal(transition) ? "enabled" : "disabled")} on table "
					+ $"'{transition.Name}' in the same migration as {column.Description} on that table. "
					+ "The temporal apparatus mirrors the target column shape, but EF orders the table "
					+ "alteration and the column operations independently, so one of the two would run "
					+ "against a shape that does not exist yet. Scaffold the temporality transition as its "
					+ "own migration, separate from column changes.");

			if (RenamedFrom(transition.Schema, transition.Name) is { } renamedFrom)
				throw new InvalidOperationException(
					$"Temporality is being {(IsMarkedTemporal(transition) ? "enabled" : "disabled")} on table "
					+ $"'{transition.Name}' in the same migration that renames it from '{renamedFrom.Table}'. "
					+ "The transition carries the table's target name, so EF orders the rename and the marker "
					+ "transition independently of what apparatus already exists: enabling would fire the "
					+ "rename choreography against apparatus that was never built under the old name, and "
					+ "disabling would strand teardown against a name the apparatus was never renamed into. "
					+ "Scaffold the temporality transition as its own migration, separate from the rename.");
		}
	}

	// The batch's rename map is keyed by old (schema, table); a marker transition's own name is always the
	// new one, so this is a reverse lookup by value rather than a TryGetValue against the map's own key.
	(string? Schema, string Table)? RenamedFrom(string? schema, string table)
	{
		foreach (var (oldName, newName) in _renames)
			if (newName == (schema, table))
				return oldName;
		return null;
	}

	static (string Table, string? Schema, string Description)? ColumnOperationTarget(MigrationOperation operation) =>
		operation switch
		{
			AddColumnOperation add => (add.Table, add.Schema, $"AddColumn '{add.Name}'"),
			DropColumnOperation drop => (drop.Table, drop.Schema, $"DropColumn '{drop.Name}'"),
			RenameColumnOperation rename => (rename.Table, rename.Schema,
				$"RenameColumn '{rename.Name}' to '{rename.NewName}'"),
			AlterColumnOperation alter => (alter.Table, alter.Schema, $"AlterColumn '{alter.Name}'"),
			_ => null
		};

	static (IReadOnlyList<(string Name, string StoreType, bool IsNullable)> Columns, IReadOnlyList<string> PkColumns)
		TargetTableShape(IModel? model, string table, string? schema)
	{
		var target = model?.GetRelationalModel().FindTable(table, schema)
			?? throw new InvalidOperationException(
				$"Table '{table}' is temporal, but the target model has no such table; the history mirror cannot be projected.");
		IReadOnlyList<(string Name, string StoreType, bool IsNullable)> columns =
			[.. target.Columns.Select(column => (column.Name, column.StoreType, column.IsNullable))];
		IReadOnlyList<string> pkColumns = target.PrimaryKey?.Columns.Select(column => column.Name).ToArray()
			?? throw new InvalidOperationException(
				$"Temporal table '{table}' has no primary key; the WITHOUT OVERLAPS history key requires one.");
		return (columns, pkColumns);
	}

	static bool IsEnableTransition(AlterTableOperation operation) =>
		IsMarkedTemporal(operation) && !IsMarkedTemporal(operation.OldTable);

	static bool IsMarkedTemporal(IReadOnlyAnnotatable annotatable) =>
		annotatable.FindAnnotation(NorseAnnotationNames.Temporal)?.Value as bool? == true;

	static bool IsTemporal(IModel? model, string table, string? schema) =>
		model is not null
		&& model.GetEntityTypes().Any(entityType =>
			entityType.GetTableName() == table
			&& entityType.GetSchema() == schema
			&& entityType.FindAnnotation(NorseAnnotationNames.Temporal)?.Value as bool? == true);

	static string StoreTypeOf(ColumnOperation column) =>
		column.ColumnType ?? throw new InvalidOperationException(
			$"Column '{column.Name}' on temporal table '{column.Table}' carries no store type; the history mirror cannot be projected without one.");

	// The one place the apparatus's schema is decided: the operation's own, else the model's default,
	// else PostgreSQL's. The last of the three is the case the create path's schema assert guards.
	static string SchemaOf(string? operationSchema, IModel? model) =>
		operationSchema ?? model?.GetDefaultSchema() ?? DefaultSchema;

	static void AppendBlock(MigrationCommandListBuilder builder, string sql)
	{
		builder.AppendLine(sql);
		builder.EndCommand();
	}

	// How many of a temporal table's column operations this batch still has to emit, whether its timeline
	// view has already been dropped for them, and whether the batch's rename of this table is still ahead.
	readonly record struct ColumnEvolution(int Remaining, bool ViewDropped, bool RenamePending);
}
