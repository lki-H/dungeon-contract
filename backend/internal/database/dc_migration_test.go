package database

import (
	"context"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"game-realtime-gm/backend/internal/config"
)

func TestDemoMigrationRefusesPartialDDL(test *testing.T) {
	if os.Getenv("DC_INTEGRATION") != "1" {
		test.Skip("dedicated Demo database required")
	}
	configuration := config.Load()
	if err := config.ValidateDemo(configuration); err != nil {
		test.Fatal(err)
	}
	ctx := context.Background()
	db, err := NewMySQLDB(ctx, configuration.Database)
	if err != nil {
		test.Fatal(err)
	}
	first := "CREATE TABLE IF NOT EXISTS pve_dc_check_one (\n    id VARCHAR(64) NOT NULL,\n    PRIMARY KEY (id)\n) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;"
	second := "CREATE TABLE IF NOT EXISTS pve_dc_check_two (\n    id VARCHAR(64) NOT NULL,\n    PRIMARY KEY (id)\n) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;"
	if _, err := db.ExecContext(ctx, first); err != nil {
		test.Fatal(err)
	}
	test.Cleanup(func() {
		_, _ = db.ExecContext(ctx, "DROP TABLE IF EXISTS pve_dc_check_one")
		_, _ = db.ExecContext(ctx, "DROP TABLE IF EXISTS pve_dc_check_two")
		_, _ = db.ExecContext(ctx, "DELETE FROM schema_migrations WHERE migration_id='p0_partial_control'")
		db.Close()
	})
	path := filepath.Join(test.TempDir(), "partial.sql")
	if err := os.WriteFile(path, []byte(first+"\n"+second), 0600); err != nil {
		test.Fatal(err)
	}
	if _, err := applyNumberedMigration(ctx, db, path, "p0_partial_control", inspectV2Tables); err == nil || !strings.Contains(err.Error(), "partial") {
		test.Fatal("partial migration accepted", err)
	}
	var tables int
	if err := db.QueryRowContext(ctx, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='pve_dc_check_two'").Scan(&tables); err != nil || tables != 0 {
		test.Fatal("partial migration executed another statement", err)
	}
}

func TestDemoV3P0MigrationIsIdempotentAndHasConstraints(test *testing.T) {
	if os.Getenv("DC_INTEGRATION") != "1" {
		test.Skip("dedicated Demo database required")
	}
	configuration := config.Load()
	if err := config.ValidateDemo(configuration); err != nil {
		test.Fatal(err)
	}
	ctx := context.Background()
	db, err := NewMySQLDB(ctx, configuration.Database)
	if err != nil {
		test.Fatal(err)
	}
	defer db.Close()
	path := filepath.Join("migrations", "day44_dc_v3_p0_clock.sql")
	first, err := ApplyDemoV3P0(ctx, db, path)
	if err != nil {
		test.Fatal(err)
	}
	second, err := ApplyDemoV3P0(ctx, db, path)
	if err != nil {
		test.Fatal(err)
	}
	if first != second || (second != "applied" && second != "reconciled") {
		test.Fatalf("unexpected migration statuses: first=%s second=%s", first, second)
	}
	for _, table := range []string{"pve_dc_v3_run_clocks", "pve_dc_v3_pause_intervals"} {
		var count int
		if err := db.QueryRowContext(ctx, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name=?", table).Scan(&count); err != nil || count != 1 {
			test.Fatalf("missing v3 table %s: count=%d err=%v", table, count, err)
		}
	}
	for _, constraint := range []struct {
		table, name, expected string
	}{
		{"pve_dc_v3_run_clocks", "chk_dc_v3_clock_protocol", "protocol_version=3"},
		{"pve_dc_v3_run_clocks", "chk_dc_v3_clock_players", "initial_player_countbetween1and4"},
		{"pve_dc_v3_run_clocks", "chk_dc_v3_clock_elapsed", "game_elapsed_ms<=wall_elapsed_ms"},
		{"pve_dc_v3_run_clocks", "chk_dc_v3_clock_pause", "manual_pause_ms<=wall_elapsed_ms"},
		{"pve_dc_v3_pause_intervals", "chk_dc_v3_pause_status", "statusin('open','closed','aborted')"},
	} {
		var count int
		if err := db.QueryRowContext(ctx, "SELECT COUNT(*) FROM information_schema.table_constraints WHERE table_schema=DATABASE() AND table_name=? AND constraint_name=? AND constraint_type='CHECK'", constraint.table, constraint.name).Scan(&count); err != nil || count != 1 {
			test.Fatalf("missing check constraint %s.%s: count=%d err=%v", constraint.table, constraint.name, count, err)
		}
		var clause string
		if err := db.QueryRowContext(ctx, "SELECT COALESCE(check_clause,'') FROM information_schema.check_constraints WHERE constraint_schema=DATABASE() AND constraint_name=?", constraint.name).Scan(&clause); err != nil {
			test.Fatal(err)
		}
		if normalizeCheckClause(clause) != constraint.expected {
			test.Fatalf("check constraint %s.%s has unexpected clause: raw=%q normalized=%q expected=%q", constraint.table, constraint.name, clause, normalizeCheckClause(clause), constraint.expected)
		}
	}
	var uniqueCount int
	if err := db.QueryRowContext(ctx, "SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='pve_dc_v3_pause_intervals' AND index_name='uq_dc_v3_pause_operation' AND non_unique=0").Scan(&uniqueCount); err != nil || uniqueCount != 1 {
		test.Fatalf("missing pause operation uniqueness: count=%d err=%v", uniqueCount, err)
	}

	data, err := os.ReadFile(path)
	if err != nil {
		test.Fatal(err)
	}
	tamperedPath := filepath.Join(test.TempDir(), "day44-tampered.sql")
	if err := os.WriteFile(tamperedPath, append(data, '\n'), 0600); err != nil {
		test.Fatal(err)
	}
	if _, err := ApplyDemoV3P0(ctx, db, tamperedPath); err == nil || !strings.Contains(err.Error(), "checksum changed") {
		test.Fatalf("tampered migration was accepted: %v", err)
	}
}
