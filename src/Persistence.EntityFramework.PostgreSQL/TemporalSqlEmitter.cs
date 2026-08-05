namespace Norse.Persistence.EntityFramework.PostgreSQL;

/// <summary>
/// The literal SQL of the PostgreSQL temporal apparatus (Norns Model B), one method per element of
/// the emission order in the temporal-tables chassis design §3.1, with the clock and version-closure
/// semantics of §3.2. Every method returns a complete, self-contained statement block; nothing here
/// touches EF metadata, so the same text serves the create path, the enable/disable transitions, and
/// every evolution operation. <c>now()</c> appears nowhere: it is transaction-start time and cannot
/// close versions safely (§3.2).
/// </summary>
static class TemporalSqlEmitter
{
	/// <summary>
	/// Step 0 — the PostgreSQL 19 floor. <c>WITHOUT OVERLAPS</c> temporal primary keys are a PG19
	/// feature; a migration reaching an older server fails here rather than part-way through the
	/// apparatus. Emitted once per migration that contains temporal DDL.
	/// </summary>
	/// <param name="assertDefaultSchemaIsPublic">
	/// Whether to also assert that the session's default schema is <c>public</c>. Passed
	/// <see langword="true"/> when some temporal table in this migration resolved no schema from
	/// either the operation or the model, so the apparatus had to be qualified with PostgreSQL's own
	/// default. The main table in that case is emitted unqualified and lands wherever the search path
	/// points; if that is not <c>public</c>, the table and its apparatus would silently split across
	/// two schemas. The assert makes the migration fail at the top instead. No default: the caller
	/// always knows whether it resolved a real schema, and guessing here is the very failure being
	/// closed.
	/// </param>
	public static string FloorAssert(bool assertDefaultSchemaIsPublic) =>
		$"""
		DO $norse$
		BEGIN
			IF pg_catalog.current_setting('server_version_num')::int < 190000 THEN
				RAISE EXCEPTION 'Norse temporal tables require PostgreSQL 19 or later (server_version_num >= 190000); this server reports %.', pg_catalog.current_setting('server_version');
			END IF;
		{(assertDefaultSchemaIsPublic ? DefaultSchemaAssert : "")}END $norse$;
		""";

	const string DefaultSchemaAssert =
		"""
			IF pg_catalog.current_schema() <> 'public' THEN
				RAISE EXCEPTION 'This migration declares no schema for its temporal tables, so the Norse temporal apparatus is qualified with PostgreSQL''s default schema (public), but the session default schema is %. Declare the schema explicitly (HasDefaultSchema or ToTable) so the table and its apparatus cannot land apart.', pg_catalog.current_schema();
			END IF;

		""";

	/// <summary>
	/// Step 1 — the <c>btree_gist</c> prerequisite for <c>WITHOUT OVERLAPS</c>, and the operational
	/// privilege boundary around it. Idempotent and loud: extension present, proceed; absent, create
	/// it; creation denied, raise the provisioning-prerequisite diagnostic so the failure lands here
	/// instead of mid-apparatus. Emitted once per migration that contains temporal DDL.
	/// </summary>
	public static string BtreeGistGuard() =>
		"""
		DO $norse$
		BEGIN
			IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_extension WHERE extname = 'btree_gist') THEN
				BEGIN
					CREATE EXTENSION btree_gist;
				EXCEPTION
					WHEN insufficient_privilege THEN
						RAISE EXCEPTION 'The btree_gist extension is a Norse platform provisioning prerequisite in this environment: the migration role may not CREATE EXTENSION. Install btree_gist out-of-band, then rerun this migration.';
				END;
			END IF;
		END $norse$;
		""";

	/// <summary>
	/// Step 2 — the database-owned system period on the main table. The default is
	/// <c>clock_timestamp()</c>, never <c>now()</c>, so an insert's open bound is wall clock at the
	/// row, not at the transaction (§3.2). The column is outside the EF model by design, which is why
	/// it arrives as an <c>ALTER TABLE</c> after the provider's own <c>CREATE TABLE</c>.
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name.</param>
	public static string SystemPeriodColumn(string schema, string table) =>
		$"""
		ALTER TABLE "{schema}"."{table}" ADD COLUMN system_period tstzrange NOT NULL DEFAULT tstzrange(clock_timestamp(), 'infinity');
		""";

	/// <summary>
	/// Step 3 — the history table: the main table's columns plus <c>system_period</c>, under a
	/// <c>PRIMARY KEY (… WITHOUT OVERLAPS)</c> that makes version overlap structurally impossible.
	/// Columns follow the projection rule (§3.4): name and store type only, nullable except the
	/// temporal key components, and never a default, identity, foreign key, check, or index.
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name.</param>
	/// <param name="columns">The main table's columns, excluding <c>system_period</c>.</param>
	/// <param name="pkColumns">The main table's primary-key column names, in key order.</param>
	public static string HistoryTable(string schema, string table,
		IReadOnlyList<(string Name, string StoreType, bool IsNullable)> columns,
		IReadOnlyList<string> pkColumns)
	{
		var keyColumns = pkColumns.ToHashSet(StringComparer.Ordinal);
		var columnLines = string.Join(Environment.NewLine, columns.Select(column =>
			$"\t\"{column.Name}\" {column.StoreType}{(keyColumns.Contains(column.Name) ? " NOT NULL" : "")},"));
		// Quoted, exactly like the definitions above: a PK column carrying mixed case (an explicit
		// HasColumnName, or a rewriter other than lower snake) would otherwise fold to lowercase here
		// and fail to match its own quoted definition.
		var keyList = string.Join(", ", pkColumns.Select(column => $"\"{column}\""));
		return $$"""
			CREATE TABLE "{{schema}}"."{{table}}_history" (
			{{columnLines}}
				"system_period" tstzrange NOT NULL,
				PRIMARY KEY ({{keyList}}, "system_period" WITHOUT OVERLAPS)
			);
			""";
	}

	/// <summary>
	/// Step 4a — the versioning function. Closure clamps to
	/// <c>greatest(clock_timestamp(), lower(OLD.system_period) + 1 microsecond)</c> so every history
	/// period has strictly positive length regardless of wall-clock behavior, and a no-op UPDATE
	/// (compared over the explicit application column list) writes no history row at all (§3.2).
	/// Hardened per the definer checklist: <c>SECURITY DEFINER</c>, <c>search_path</c> pinned to
	/// <c>pg_catalog</c>, every reference schema-qualified, execute revoked from <c>PUBLIC</c>.
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name.</param>
	/// <param name="columns">The main table's columns, excluding <c>system_period</c>.</param>
	/// <param name="orReplace">
	/// Whether to emit <c>CREATE OR REPLACE</c>. Evolution passes <see langword="true"/>: replacing the
	/// function in place keeps the existing triggers bound to it, which is exactly why a table rename —
	/// where the function's name changes and the old triggers would stay bound to the old function —
	/// retires the apparatus explicitly instead (§3.4). No default: the two callers mean opposite things
	/// and neither is the obvious one.
	/// </param>
	public static string TriggerFunction(string schema, string table,
		IReadOnlyList<(string Name, string StoreType, bool IsNullable)> columns, bool orReplace)
	{
		var columnList = string.Join(", ", columns.Select(column => $"\"{column.Name}\""));
		var oldColumnList = string.Join(", ", columns.Select(column => $"OLD.\"{column.Name}\""));
		var newColumnList = string.Join(", ", columns.Select(column => $"NEW.\"{column.Name}\""));
		return $$"""
			CREATE {{(orReplace ? "OR REPLACE " : "")}}FUNCTION "{{schema}}"."{{table}}_versioning"() RETURNS trigger
			LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog AS $norse$
			DECLARE ts timestamptz;
			BEGIN
				IF TG_OP = 'UPDATE' AND ROW({{oldColumnList}}) IS NOT DISTINCT FROM ROW({{newColumnList}}) THEN
					RETURN NEW;
				END IF;
				ts := greatest(pg_catalog.clock_timestamp(), pg_catalog.lower(OLD.system_period) + interval '1 microsecond');
				INSERT INTO "{{schema}}"."{{table}}_history" ({{columnList}}, system_period)
					VALUES ({{oldColumnList}}, pg_catalog.tstzrange(pg_catalog.lower(OLD.system_period), ts));
				IF TG_OP = 'UPDATE' THEN
					NEW.system_period := pg_catalog.tstzrange(ts, 'infinity');
					RETURN NEW;
				END IF;
				RETURN OLD;
			END $norse$;
			REVOKE EXECUTE ON FUNCTION "{{schema}}"."{{table}}_versioning"() FROM PUBLIC;
			""";
	}

	/// <summary>
	/// Step 4b — the UPDATE and DELETE triggers binding the main table to its versioning function.
	/// Separate from <see cref="TriggerFunction"/> because evolution regenerates the function with
	/// <c>CREATE OR REPLACE</c> and leaves the triggers alone (§3.4).
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name.</param>
	public static string Triggers(string schema, string table) =>
		$"""
		CREATE TRIGGER "{table}_versioning_update" BEFORE UPDATE ON "{schema}"."{table}"
			FOR EACH ROW EXECUTE FUNCTION "{schema}"."{table}_versioning"();
		CREATE TRIGGER "{table}_versioning_delete" BEFORE DELETE ON "{schema}"."{table}"
			FOR EACH ROW EXECUTE FUNCTION "{schema}"."{table}_versioning"();
		""";

	/// <summary>
	/// Step 5 — the timeline view: current versions unioned with closed ones. Explicit column lists,
	/// never <c>SELECT *</c>, so the view's shape is the migration's shape.
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name.</param>
	/// <param name="columns">The main table's columns, excluding <c>system_period</c>.</param>
	public static string TimelineView(string schema, string table,
		IReadOnlyList<(string Name, string StoreType, bool IsNullable)> columns)
	{
		var columnList = string.Join(", ", columns.Select(column => $"\"{column.Name}\""));
		return $$"""
			CREATE VIEW "{{schema}}"."{{table}}_timeline" AS
			SELECT {{columnList}}, system_period FROM "{{schema}}"."{{table}}"
			UNION ALL
			SELECT {{columnList}}, system_period FROM "{{schema}}"."{{table}}_history";
			""";
	}

	/// <summary>
	/// The enable transition (§3.3): temporality added to a table that already exists and already holds
	/// rows. Everything from step 3 on is the create path verbatim — this method composes those same
	/// emitters — and only the arrival of <c>system_period</c> differs, for one reason: the column
	/// cannot arrive carrying the create path's volatile default, which would scatter each pre-existing
	/// row's open bound across the table rewrite. Instead the column arrives nullable, a single
	/// <c>DO</c> block captures <c>clock_timestamp()</c> <em>once</em> and stamps every existing row
	/// from that one reading, and only then does the column take <c>NOT NULL</c> and the standing
	/// default that rows inserted afterwards use. History starts empty: the table's pre-temporal past
	/// is honestly unrecorded, and every pre-existing row enters the timeline as a current version
	/// opened at the enable timestamp. Emitted as one block because the nullable window between the
	/// <c>ADD COLUMN</c> and the <c>SET NOT NULL</c> is choreography, not a sequence of independent
	/// steps.
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name.</param>
	/// <param name="columns">The main table's columns, excluding <c>system_period</c>.</param>
	/// <param name="pkColumns">The main table's primary-key column names, in key order.</param>
	public static string EnableTransition(string schema, string table,
		IReadOnlyList<(string Name, string StoreType, bool IsNullable)> columns,
		IReadOnlyList<string> pkColumns) =>
		$"""
		ALTER TABLE "{schema}"."{table}" ADD COLUMN system_period tstzrange;
		DO $norse$
		DECLARE ts timestamptz;
		BEGIN
			ts := pg_catalog.clock_timestamp();
			UPDATE "{schema}"."{table}" SET system_period = pg_catalog.tstzrange(ts, 'infinity') WHERE system_period IS NULL;
		END $norse$;
		ALTER TABLE "{schema}"."{table}" ALTER COLUMN system_period SET NOT NULL;
		ALTER TABLE "{schema}"."{table}" ALTER COLUMN system_period SET DEFAULT tstzrange(clock_timestamp(), 'infinity');

		{HistoryTable(schema, table, columns, pkColumns)}

		{TriggerFunction(schema, table, columns, orReplace: false)}

		{Triggers(schema, table)}

		{TimelineView(schema, table, columns)}
		""";

	/// <summary>
	/// The disable transition (§3.3): temporality removed from a table that has it. Recorded history is
	/// destroyed, and the scaffolded migration says so in plain statements rather than behind a helper
	/// call — the same visible-destruction posture as dropping the entity (§3.4). The order is the only
	/// one PostgreSQL accepts: the triggers depend on the function, and the view depends on both the
	/// history table and <c>system_period</c>.
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name.</param>
	public static string DisableTransition(string schema, string table) =>
		$"""
		{DropTriggersAndFunction(schema, table, table)}
		{DropTimelineView(schema, table)}
		{DropHistoryTable(schema, table)}
		ALTER TABLE "{schema}"."{table}" DROP COLUMN system_period;
		""";

	/// <summary>
	/// Destroys the recorded history. Emitted by the disable transition (§3.3) and by the drop of the
	/// entity itself (§3.4) — the two acts the design treats identically, and the two it insists stay
	/// visible in the scaffolded diff as a plain <c>DROP TABLE</c> rather than hiding behind a helper.
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name.</param>
	public static string DropHistoryTable(string schema, string table) =>
		$"""DROP TABLE "{schema}"."{table}_history";""";

	/// <summary>
	/// Evolution step 1 (§3.4): the timeline view goes first, every time. PostgreSQL refuses to drop a
	/// column or change its type while a view selects it, and <c>CREATE OR REPLACE VIEW</c> cannot change
	/// the output column set — so the view is dropped and recreated afresh, never replaced in place.
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name.</param>
	public static string DropTimelineView(string schema, string table) =>
		$"""DROP VIEW "{schema}"."{table}_timeline";""";

	/// <summary>
	/// Retires the versioning triggers and the function they are bound to. PostgreSQL keeps a renamed
	/// table's triggers under their original names, still bound to the original function, so a rename
	/// drops them by their old names off the already-renamed table and creates newly named ones — the
	/// apparatus is never left bound to a stale function (§3.4).
	/// </summary>
	/// <param name="schema">The schema holding the table and the function.</param>
	/// <param name="table">The table the triggers currently sit on — the new name during a rename.</param>
	/// <param name="apparatusName">
	/// The name the apparatus objects were derived from. The same as <paramref name="table"/> everywhere
	/// except immediately after a rename, when the objects still carry their pre-rename names.
	/// </param>
	public static string DropTriggersAndFunction(string schema, string table, string apparatusName) =>
		$"""
		DROP TRIGGER "{apparatusName}_versioning_update" ON "{schema}"."{table}";
		DROP TRIGGER "{apparatusName}_versioning_delete" ON "{schema}"."{table}";
		DROP FUNCTION "{schema}"."{apparatusName}_versioning"();
		""";

	/// <summary>
	/// Renames the history table alongside its main table — a rename, never a rebuild, so the recorded
	/// history's data mapping is preserved (§3.4).
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name before the rename.</param>
	/// <param name="newTable">The main table's name after the rename.</param>
	public static string RenameHistoryTable(string schema, string table, string newTable) =>
		$"""ALTER TABLE "{schema}"."{table}_history" RENAME TO "{newTable}_history";""";

	/// <summary>
	/// Mirrors an added column onto history per the projection rule (§3.4): name and store type only,
	/// nullable regardless of what the main column declares, and never the default, identity, or
	/// generation expression the main column may carry. History rows predating the column say NULL,
	/// honestly.
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name.</param>
	/// <param name="column">The added column's name.</param>
	/// <param name="storeType">The added column's store type.</param>
	public static string AddHistoryColumn(string schema, string table, string column, string storeType) =>
		$"""ALTER TABLE "{schema}"."{table}_history" ADD COLUMN "{column}" {storeType};""";

	/// <summary>
	/// Mirrors a dropped column onto history. History is a version log, not an archive of dead columns
	/// (§3.4) — a retiring column's historical values go with it, deliberately.
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name.</param>
	/// <param name="column">The dropped column's name.</param>
	public static string DropHistoryColumn(string schema, string table, string column) =>
		$"""ALTER TABLE "{schema}"."{table}_history" DROP COLUMN "{column}";""";

	/// <summary>
	/// Mirrors a renamed column onto history — a rename, never a drop plus an add, so the recorded
	/// values follow the column (§3.4).
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name.</param>
	/// <param name="column">The column's name before the rename.</param>
	/// <param name="newColumn">The column's name after the rename.</param>
	public static string RenameHistoryColumn(string schema, string table, string column, string newColumn) =>
		$"""ALTER TABLE "{schema}"."{table}_history" RENAME COLUMN "{column}" TO "{newColumn}";""";

	/// <summary>
	/// Mirrors a store-type change onto history. Type only: nullability is never projected, because a
	/// history column is nullable whatever the main column declares, and the temporal key components are
	/// <c>NOT NULL</c> from the day the history table was created (§3.4).
	/// </summary>
	/// <param name="schema">The main table's schema.</param>
	/// <param name="table">The main table's name.</param>
	/// <param name="column">The altered column's name.</param>
	/// <param name="storeType">The altered column's new store type.</param>
	public static string AlterHistoryColumnType(string schema, string table, string column, string storeType) =>
		$"""ALTER TABLE "{schema}"."{table}_history" ALTER COLUMN "{column}" TYPE {storeType};""";
}
