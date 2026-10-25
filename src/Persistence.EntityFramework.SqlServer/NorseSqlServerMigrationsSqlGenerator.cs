using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.SqlServer.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Update;

namespace Norse.Persistence.EntityFramework.SqlServer;

// EF1001: SqlServerAnnotationNames is EF-internal by attribute; read only to refuse a memory-optimized
// principal table rather than silently dropping its MEMORY_OPTIMIZED clause, to recognize an added identity
// column, and to recognize EF-native temporality on the far side of a split or unsplit.
#pragma warning disable EF1001

/// <summary>
///     Emits engine-native system-versioning for the principal table of a split entity that declared
///     <c>TemporalOnPrincipalTableOnly()</c> (spec §6.1–§6.2), around SQL Server's own migration SQL. The
///     entity is not EF-temporal, so EF's own temporal emission never fires for it; instead every operation
///     on its principal table carries <see cref="NorseAnnotationNames.TemporalPrincipalTableOnly" /> —
///     create and alter because <see cref="NorseSqlServerAnnotationProvider" /> projects it onto the
///     relational table and the differ copies table annotations onto them, drop because
///     <see cref="NorseSqlServerMigrationsAnnotationProvider" /> forwards it through <c>ForRemove</c>. Reading
///     the stamp off the operation rather than the model is also what makes a scaffolded migration replay:
///     the scaffolder writes table annotations into the migration's own code. Fragment tables never carry the
///     stamp and get SQL Server's stock DDL. The period columns are <c>HIDDEN</c> and absent from the EF
///     model (PoC G2), so they exist in DDL only, under the names EF-native temporality uses. Column
///     operations on the principal table need nothing from here: SQL Server propagates them to history while
///     versioning stays on (G5), so SQL Server's stock DDL is already the §6.2 emission. What the engine or
///     the history association cannot survive is refused by name before a line of SQL is emitted (see
///     <c>GuardRejectedOperations</c>), mirroring the PostgreSQL generator's rejections.
/// </summary>
/// <param name="dependencies">The migrations SQL generator dependencies.</param>
/// <param name="commandBatchPreparer">The command batch preparer, forwarded to the base generator.</param>
sealed class NorseSqlServerMigrationsSqlGenerator(
	MigrationsSqlGeneratorDependencies dependencies,
	ICommandBatchPreparer commandBatchPreparer)
	: SqlServerMigrationsSqlGenerator(dependencies, commandBatchPreparer)
{
	const string PeriodStart = "SystemPeriodStart";

	const string PeriodEnd = "SystemPeriodEnd";

	// SQL Server's own default schema. HISTORY_TABLE demands a two-part name, so the history table is always
	// qualified — with the operation's schema, else the model's default, else this (spec §6.2).
	const string DefaultSchema = "dbo";

	// The open bound of a current row: datetime2's maximum, the value the engine itself writes (G5).
	const string PeriodEndMaximum = "CONVERT(datetime2, '9999-12-31 23:59:59.9999999')";

	// The tail of every rejection but the computed/identity add, which has a manual sequence of its own.
	const string SanctionedPath =
		"The sanctioned path is deliberate: disable versioning first (remove ITemporalEntity — visible destruction "
		+ "of the recorded history), perform the change in its own migration, then re-enable; or author the "
		+ "migration by hand for the rare case that must preserve history across the change.";

	/// <inheritdoc />
	public override IReadOnlyList<MigrationCommand> Generate(IReadOnlyList<MigrationOperation> operations,
		IModel? model = null, MigrationsSqlGenerationOptions options = MigrationsSqlGenerationOptions.Default)
	{
		GuardRejectedOperations(operations, model);
		return base.Generate(operations, model, options);
	}

	/// <inheritdoc />
	/// <remarks>
	///     Inline, one statement (spec §6.1, PoC G5 "create"): the hidden period columns and
	///     <c>PERIOD FOR SYSTEM_TIME</c> follow the model's columns, the constraints follow those, and the
	///     versioning clause trails the column list. The engine creates the history table itself. Column and
	///     table comments are emitted exactly as SQL Server's own generator emits them, after the statement.
	/// </remarks>
	protected override void Generate(CreateTableOperation operation, IModel? model,
		MigrationCommandListBuilder builder, bool terminate = true)
	{
		if (!IsPrincipalTableOnly(operation))
		{
			base.Generate(operation, model, builder, terminate);
			return;
		}

		if (operation[SqlServerAnnotationNames.MemoryOptimized] as bool? == true)
			throw new InvalidOperationException(
				$"Table '{operation.Name}' is memory-optimized; TemporalOnPrincipalTableOnly() supports disk-based principal tables only.");

		var hasComments = operation.Comment is not null || operation.Columns.Any(column => column.Comment is not null);
		if (!terminate && hasComments)
			throw new ArgumentException(
				$"Unterminated SQL cannot be produced for the {nameof(CreateTableOperation)} of '{operation.Name}' because it carries comments.");

		var helper = Dependencies.SqlGenerationHelper;
		builder
			.Append("CREATE TABLE ")
			.Append(helper.DelimitIdentifier(operation.Name, operation.Schema))
			.AppendLine(" (");
		using (builder.Indent())
		{
			CreateTableColumns(operation, model, builder);
			builder.AppendLine(",");
			AppendPeriodColumns(builder);
			builder.Append(PeriodDefinition());
			CreateTableConstraints(operation, model, builder);
			builder.AppendLine();
		}

		builder
			.AppendLine(")")
			.Append($"WITH ({VersioningOn(operation.Name, operation.Schema, model)})");
		if (!terminate)
			return;

		builder.AppendLine(helper.StatementTerminator);
		AppendComments(operation, builder);
		EndStatement(builder);
	}

	/// <inheritdoc />
	/// <remarks>
	///     SQL Server refuses to drop a system-versioned table (13552), so versioning goes off first, then the
	///     history table, then the table — one batch, the ordering G5 confirmed (either drop order is appliable
	///     once versioning is off). Recorded history dies with the entity: visible destruction, as on PostgreSQL.
	/// </remarks>
	protected override void Generate(DropTableOperation operation, IModel? model,
		MigrationCommandListBuilder builder, bool terminate = true)
	{
		if (IsPrincipalTableOnly(operation))
		{
			var helper = Dependencies.SqlGenerationHelper;
			builder
				.AppendLine($"{AlterTable(operation.Name, operation.Schema)} SET (SYSTEM_VERSIONING = OFF){helper.StatementTerminator}")
				.AppendLine($"DROP TABLE {HistoryTable(operation.Name, operation.Schema, model)}{helper.StatementTerminator}");
		}

		base.Generate(operation, model, builder, terminate);
	}

	/// <inheritdoc />
	/// <remarks>
	///     The opt-in transitions of §6.2. They exist as operations at all only because
	///     <see cref="NorseSqlServerAnnotationProvider" /> projects the stamp onto the table; on the disable side
	///     the target table no longer carries it, so <see cref="AlterTableOperation.OldTable" /> is the only
	///     record that it was ever versioned. An alteration that leaves the stamp as it found it is no
	///     transition and gets nothing beyond SQL Server's own emission. Each direction is one command — one
	///     batch, as G5 proved appliable — never split by a script's <c>GO</c>.
	/// </remarks>
	protected override void Generate(AlterTableOperation operation, IModel? model,
		MigrationCommandListBuilder builder)
	{
		base.Generate(operation, model, builder);
		var isVersioned = IsPrincipalTableOnly(operation);
		if (isVersioned == IsPrincipalTableOnly(operation.OldTable))
			return;

		if (isVersioned)
			AppendEnable(operation, model, builder);
		else
			AppendDisable(operation, model, builder);
		EndStatement(builder);
	}

	// Enable on a populated table (G5): the period columns arrive with named defaults, the only mechanism that
	// can fill GENERATED ALWAYS columns on existing rows (4901 without them). SYSUTCDATETIME() is evaluated once
	// per statement, so every pre-existing row opens at one enable timestamp — the chassis's PostgreSQL enable
	// semantics (chassis spec §3.3). The engine creates the history table empty: the table's pre-temporal past
	// is honestly unrecorded.
	void AppendEnable(AlterTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
	{
		var terminator = Dependencies.SqlGenerationHelper.StatementTerminator;
		var table = AlterTable(operation.Name, operation.Schema);
		builder.AppendLine($"{table} ADD");
		using (builder.Indent())
		{
			AppendPeriodColumns(builder, enabledTable: operation.Name);
			builder.AppendLine($"{PeriodDefinition()}{terminator}");
		}

		builder.AppendLine($"{table} SET ({VersioningOn(operation.Name, operation.Schema, model)}){terminator}");
	}

	// Disable, one unit (G5): after DROP PERIOD the formerly hidden columns are ordinary visible columns, so a
	// disable that stopped there would leave them in SELECT *. The defaults exist only on a table that was
	// enabled rather than created, and DROP COLUMN fails on them (5074) — hence IF EXISTS, under exactly the
	// names AppendEnable gives them. Recorded history is destroyed, as on PostgreSQL (chassis spec §3.3): the
	// same visible destruction as dropping the entity.
	void AppendDisable(AlterTableOperation operation, IModel? model, MigrationCommandListBuilder builder)
	{
		var terminator = Dependencies.SqlGenerationHelper.StatementTerminator;
		var table = AlterTable(operation.Name, operation.Schema);
		builder
			.AppendLine($"{table} SET (SYSTEM_VERSIONING = OFF){terminator}")
			.AppendLine($"{table} DROP PERIOD FOR SYSTEM_TIME{terminator}")
			.AppendLine($"{table} DROP CONSTRAINT IF EXISTS {Delimit(DefaultConstraint(operation.Name, PeriodStart))}{terminator}")
			.AppendLine($"{table} DROP CONSTRAINT IF EXISTS {Delimit(DefaultConstraint(operation.Name, PeriodEnd))}{terminator}")
			.AppendLine($"{table} DROP COLUMN {Delimit(PeriodStart)}, {Delimit(PeriodEnd)}{terminator}")
			.AppendLine($"DROP TABLE {HistoryTable(operation.Name, operation.Schema, model)}{terminator}");
	}

	// Caught across the whole batch before any SQL is emitted, so the diagnostic never depends on where EF
	// sorted the operation. Renames and split transitions go first: a rename also renames the primary-key
	// constraint, which EF expresses as a drop and an add, and that collateral must not be reported as a key
	// change.
	void GuardRejectedOperations(IReadOnlyList<MigrationOperation> operations, IModel? model)
	{
		foreach (var operation in operations)
		{
			switch (operation)
			{
				case RenameTableOperation rename
					when IsPrincipalTableOnly(model, rename.NewName ?? rename.Name, rename.NewSchema):
					throw !string.Equals(rename.Schema, rename.NewSchema, StringComparison.Ordinal) ?
						SchemaMove(rename, model) :
						Rename(rename, model);
				case AlterTableOperation alter when IsSplitTransition(alter):
					throw SplitTransition(alter, model);
				default:
					continue;
			}
		}

		foreach (var operation in operations)
		{
			switch (operation)
			{
				case AddPrimaryKeyOperation add when IsPrincipalTableOnly(model, add.Table, add.Schema):
					throw PrimaryKeyChange(add.Table, add.Schema, model);
				case DropPrimaryKeyOperation drop when IsPrincipalTableOnly(model, drop.Table, drop.Schema):
					throw PrimaryKeyChange(drop.Table, drop.Schema, model);
				case AddColumnOperation add when add.ComputedColumnSql is not null
					&& IsPrincipalTableOnly(model, add.Table, add.Schema):
					throw VersioningOffColumn("computed", 13724, add, model);
				case AddColumnOperation add when add[SqlServerAnnotationNames.Identity] is not null
					&& IsPrincipalTableOnly(model, add.Table, add.Schema):
					throw VersioningOffColumn("identity", 13704, add, model);
				default:
					continue;
			}
		}
	}

	InvalidOperationException PrimaryKeyChange(string table, string? schema, IModel? model) =>
		new($"A primary key change on system-versioned principal table '{table}' (TemporalOnPrincipalTableOnly()) "
			+ "is not supported. SQL Server refuses to drop the primary key of a system-versioned table, and every "
			+ $"row of {HistoryTable(table, schema, model)} is a version of a key that would no longer exist. "
			+ SanctionedPath);

	InvalidOperationException Rename(RenameTableOperation rename, IModel? model) =>
		new($"Renaming system-versioned principal table '{rename.Name}' to '{rename.NewName}' "
			+ "(TemporalOnPrincipalTableOnly()) is not supported. Its history table "
			+ $"{HistoryTable(rename.Name, rename.Schema, model)} and its period defaults "
			+ $"([{DefaultConstraint(rename.Name, PeriodStart)}], [{DefaultConstraint(rename.Name, PeriodEnd)}]) are "
			+ "named for it, and the disable path would go looking for them under the new name. "
			+ SanctionedPath);

	InvalidOperationException SchemaMove(RenameTableOperation rename, IModel? model) =>
		new($"A schema move to '{rename.NewSchema ?? DefaultSchema}' of system-versioned principal table "
			+ $"'{rename.Name}' (TemporalOnPrincipalTableOnly()) is not supported. HISTORY_TABLE is named in the "
			+ $"table's own schema, so {HistoryTable(rename.Name, rename.Schema, model)} would be stranded in the old "
			+ "one. " + SanctionedPath);

	// The one rejection whose manual sequence is not the disable/re-enable path: the engine refuses the add while
	// versioning is on (13724 for a computed column; 13704 for an identity one, measured against SQL Server 2025
	// 17.0 — the engine would have to project IDENTITY onto history), and the batch compiles against the
	// pre-batch state, so OFF cannot share a batch with the add (G5/G6). History holds materialized values, so
	// its column is plain — but of matching nullability, or the re-enable fails its schema check (13531).
	InvalidOperationException VersioningOffColumn(string kind, int engineError, AddColumnOperation add, IModel? model)
	{
		var table = AlterTable(add.Table, add.Schema);
		var history = HistoryTable(add.Table, add.Schema, model);
		return new($"Adding {kind} column '{add.Name}' to system-versioned principal table '{add.Table}' "
			+ "(TemporalOnPrincipalTableOnly()) is not supported: SQL Server refuses it while versioning is on "
			+ $"(Msg {engineError}), and the change cannot share a batch with the statement that turns versioning off. "
			+ "Author it by hand instead, as separate migrationBuilder.Sql batches: "
			+ $"(1) {table} SET (SYSTEM_VERSIONING = OFF); in a batch of its own; "
			+ $"(2) add the {kind} column '{add.Name}' to {Dependencies.SqlGenerationHelper.DelimitIdentifier(add.Table, add.Schema)}; "
			+ $"(3) add a plain (non-{kind}) column '{add.Name}' of the same type and nullability to {history} "
			+ "(a NOT NULL one with a default, to fill the existing history rows); "
			+ $"(4) {table} SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = {history}, DATA_CONSISTENCY_CHECK = ON));");
	}

	// EF-native temporality maps the periods as model columns and owns the history table through its own
	// annotations; the opt-in keeps them HIDDEN outside the model. Neither realization can take over the
	// other's versioning in one migration.
	InvalidOperationException SplitTransition(AlterTableOperation alter, IModel? model) =>
		new($"Table '{alter.Name}' is {(IsPrincipalTableOnly(alter) ? "splitting" : "unsplitting")} in a single "
			+ "migration, changing between EF-native temporality (an unsplit temporal entity) and "
			+ "TemporalOnPrincipalTableOnly() (a split one). The two realizations own the period columns and "
			+ $"{HistoryTable(alter.Name, alter.Schema, model)} differently — EF-native maps the periods as model "
			+ "columns, the opt-in keeps them HIDDEN outside the model — so neither can hand versioning to the other "
			+ "in place. " + SanctionedPath);

	static bool IsSplitTransition(AlterTableOperation alter) =>
		IsPrincipalTableOnly(alter) != IsPrincipalTableOnly(alter.OldTable)
		&& (IsEfNativeTemporal(alter) || IsEfNativeTemporal(alter.OldTable));

	static bool IsEfNativeTemporal(IReadOnlyAnnotatable annotatable) =>
		annotatable.FindAnnotation(SqlServerAnnotationNames.IsTemporal)?.Value as bool? == true;

	// Ordinary operations do not carry the stamp, so they identify the principal table through the target
	// model's relational table, where NorseSqlServerAnnotationProvider put it.
	static bool IsPrincipalTableOnly(IModel? model, string table, string? schema) =>
		model?.GetRelationalModel().FindTable(table, schema) is { } target && IsPrincipalTableOnly(target);

	// Created columns carry no defaults (the engine fills GENERATED ALWAYS columns itself); enabled ones carry
	// named defaults so the disable path can drop them by name.
	void AppendPeriodColumns(MigrationCommandListBuilder builder, string? enabledTable = null) =>
		builder
			.AppendLine($"{Delimit(PeriodStart)} datetime2 GENERATED ALWAYS AS ROW START HIDDEN NOT NULL"
				+ $"{PeriodDefault(enabledTable, PeriodStart, "SYSUTCDATETIME()")},")
			.AppendLine($"{Delimit(PeriodEnd)} datetime2 GENERATED ALWAYS AS ROW END HIDDEN NOT NULL"
				+ $"{PeriodDefault(enabledTable, PeriodEnd, PeriodEndMaximum)},");

	string PeriodDefault(string? enabledTable, string column, string value) =>
		enabledTable is null ?
			"" :
			$" CONSTRAINT {Delimit(DefaultConstraint(enabledTable, column))} DEFAULT {value}";

	static string DefaultConstraint(string table, string column) =>
		$"DF_{table}_{column}";

	string PeriodDefinition() =>
		$"PERIOD FOR SYSTEM_TIME ({Delimit(PeriodStart)}, {Delimit(PeriodEnd)})";

	string VersioningOn(string table, string? schema, IModel? model) =>
		$"SYSTEM_VERSIONING = ON (HISTORY_TABLE = {HistoryTable(table, schema, model)})";

	string HistoryTable(string table, string? schema, IModel? model) =>
		Dependencies.SqlGenerationHelper.DelimitIdentifier($"{table}History",
			schema ?? model?.GetDefaultSchema() ?? DefaultSchema);

	// Unqualified when the operation is, exactly as SQL Server's own generator addresses the table.
	string AlterTable(string table, string? schema) =>
		$"ALTER TABLE {Dependencies.SqlGenerationHelper.DelimitIdentifier(table, schema)}";

	string Delimit(string identifier) =>
		Dependencies.SqlGenerationHelper.DelimitIdentifier(identifier);

	// The comment tail of SQL Server's own CreateTable emission: the table's description, then each
	// column's, the variable declarations emitted once.
	void AppendComments(CreateTableOperation operation, MigrationCommandListBuilder builder)
	{
		var first = true;
		if (operation.Comment is not null)
		{
			AddDescription(builder, operation.Comment, operation.Schema, operation.Name);
			first = false;
		}

		foreach (var column in operation.Columns)
		{
			if (column.Comment is null)
				continue;
			AddDescription(builder, column.Comment, operation.Schema, operation.Name, column.Name,
				omitVariableDeclarations: !first);
			first = false;
		}
	}

	static bool IsPrincipalTableOnly(IReadOnlyAnnotatable annotatable) =>
		annotatable.FindAnnotation(NorseAnnotationNames.TemporalPrincipalTableOnly)?.Value as bool? == true;
}
