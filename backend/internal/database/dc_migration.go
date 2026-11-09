package database

import (
	"context"
	"database/sql"
)

func ApplyDemoControl(ctx context.Context, db *sql.DB, path string) (string, error) {
	return applyNumberedMigration(ctx, db, path, "day41_dc_gameplay_control", inspectV2Tables)
}

func ApplyDemoResults(ctx context.Context, db *sql.DB, path string) (string, error) {
	return applyNumberedMigration(ctx, db, path, "day42_dc_results_trials", inspectV2Tables)
}

func ApplyDemoQuality(ctx context.Context, db *sql.DB, path string) (string, error) {
	return applyNumberedMigration(ctx, db, path, "day43_dc_quality_progress", inspectV2Tables)
}

func ApplyDemoV3P0(ctx context.Context, db *sql.DB, path string) (string, error) {
	return applyNumberedMigration(ctx, db, path, "day44_dc_v3_p0_clock", inspectDemoV3P0Tables)
}

func ApplyDemoV4Sync(ctx context.Context, db *sql.DB, path string) (string, error) {
	return applyNumberedMigration(ctx, db, path, "day45_dc_v4_sync_clock", inspectDemoV4Sync)
}

func ApplyDemoV4Cooperation(ctx context.Context, db *sql.DB, path string) (string, error) {
	return applyNumberedMigration(ctx, db, path, "day46_dc_v4_cooperation", inspectDemoV4Cooperation)
}

func inspectDemoV4Cooperation(ctx context.Context, connection *sql.Conn, script string) (bool, bool, error) {
	complete, partial, err := inspectV2Tables(ctx, connection, script)
	if err != nil || !complete {
		return complete, partial, err
	}
	var enforced int
	var clause string
	if err := connection.QueryRowContext(ctx, "SELECT COUNT(*) FROM information_schema.table_constraints WHERE table_schema=DATABASE() AND table_name='pve_dc_v4_life_stats' AND constraint_name='chk_dc_v4_life_stats' AND constraint_type='CHECK' AND enforced='YES'").Scan(&enforced); err != nil {
		return false, partial, err
	}
	if err := connection.QueryRowContext(ctx, "SELECT check_clause FROM information_schema.check_constraints WHERE constraint_schema=DATABASE() AND constraint_name='chk_dc_v4_life_stats'").Scan(&clause); err != nil {
		return false, partial, err
	}
	expected := "((slot >= 0) AND (slot <= 3) AND (knockdowns >= 0) AND (rescues >= 0) AND (protected_damage >= 0) AND (control_effect_ms >= 0))"
	return enforced == 1 && normalizeCheckClause(clause) == normalizeCheckClause(expected), partial, nil
}

func inspectDemoV4Sync(ctx context.Context, connection *sql.Conn, script string) (bool, bool, error) {
	complete, partial, err := inspectV2Tables(ctx, connection, script)
	if err != nil || !complete {
		return complete, partial, err
	}
	var enforced int
	if err := connection.QueryRowContext(ctx, "SELECT COUNT(*) FROM information_schema.table_constraints WHERE table_schema=DATABASE() AND table_name='pve_dc_v4_run_clocks' AND constraint_name='chk_dc_v4_clock' AND constraint_type='CHECK' AND enforced='YES'").Scan(&enforced); err != nil {
		return false, partial, err
	}
	var clause string
	if err := connection.QueryRowContext(ctx, "SELECT check_clause FROM information_schema.check_constraints WHERE constraint_schema=DATABASE() AND constraint_name='chk_dc_v4_clock'").Scan(&clause); err != nil {
		return false, partial, err
	}
	expected := "((clock_revision > 0) AND (game_elapsed_ms >= 0) AND (manual_pause_ms >= 0) AND (wall_elapsed_ms = (game_elapsed_ms + manual_pause_ms)) AND (clock_state IN ('running','paused','technical_wait')))"
	return enforced == 1 && normalizeCheckClause(clause) == normalizeCheckClause(expected), partial, nil
}
