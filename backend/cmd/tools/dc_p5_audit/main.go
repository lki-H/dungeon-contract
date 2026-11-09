package main

import (
	"context"
	"database/sql"
	"encoding/json"
	"flag"
	"fmt"
	"game-realtime-gm/backend/internal/config"
	"game-realtime-gm/backend/internal/database"
	"os"
	"time"
)

func main() {
	if err := execute(); err != nil {
		fmt.Fprintln(os.Stderr, "P5 read-only audit failed:", err)
		os.Exit(1)
	}
}
func execute() error {
	quarantine := flag.Bool("quarantine-fixtures", false, "Move only unauthenticatable P5 publication fixtures to a separate trial namespace; preserve all facts and rewards")
	flag.Parse()
	settings := config.Load()
	if err := config.ValidateDemo(settings); err != nil {
		return err
	}
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	db, err := database.NewMySQLDB(ctx, settings.Database)
	if err != nil {
		return err
	}
	defer db.Close()
	repairs := []map[string]string{}
	if *quarantine {
		rows, err := db.QueryContext(ctx, "SELECT DISTINCT t.record_id,t.run_id FROM pve_dc_trial_records t JOIN pve_runs r ON r.id=t.run_id JOIN pve_run_participants m ON m.run_id=r.id JOIN players p ON p.id=m.player_id WHERE r.rule_version='dc_ruins.v5.p5.1' AND t.trial_version='trial.v5.1' AND t.status='published' AND p.password_hash='not_a_login_hash' AND p.nickname='P0 integration' LIMIT 100")
		if err != nil {
			return err
		}
		for rows.Next() {
			var record, runID string
			if err := rows.Scan(&record, &runID); err != nil {
				rows.Close()
				return err
			}
			repairs = append(repairs, map[string]string{"record_id": record, "run_id": runID, "before_trial_version": "trial.v5.1", "after_trial_version": "fixture.p5." + record})
		}
		err = rows.Err()
		rows.Close()
		if err != nil {
			return err
		}
		for _, repair := range repairs {
			if _, err := db.ExecContext(ctx, "UPDATE pve_dc_trial_records SET trial_version=? WHERE record_id=? AND trial_version='trial.v5.1'", repair["after_trial_version"], repair["record_id"]); err != nil {
				return err
			}
		}
	}
	rows, err := db.QueryContext(ctx, "SELECT r.id,r.status,COALESCE(r.end_reason,''),a.state,(SELECT COALESCE(SUM(amount),0) FROM pve_reward_grants g WHERE g.run_id=r.id) FROM pve_runs r JOIN pve_dc_allocations a ON a.run_id=r.id WHERE r.rule_version='dc_ruins.v5.p5.1' ORDER BY r.created_at,r.id LIMIT 1000")
	if err != nil {
		return err
	}
	type entry struct {
		Run         string `json:"run_id"`
		Status      string `json:"status"`
		Outcome     string `json:"outcome"`
		Mode        string `json:"mode"`
		Engineering bool   `json:"engineering"`
		Rewards     int64  `json:"reward_total"`
		Trial       any    `json:"trial,omitempty"`
	}
	values := []entry{}
	for rows.Next() {
		var value entry
		var data []byte
		if err := rows.Scan(&value.Run, &value.Status, &value.Outcome, &data, &value.Rewards); err != nil {
			rows.Close()
			return err
		}
		var allocation struct {
			Engineering bool `json:"engineering"`
			Manifest    struct {
				Mode string `json:"mode"`
			} `json:"manifest"`
		}
		if err := json.Unmarshal(data, &allocation); err != nil {
			rows.Close()
			return err
		}
		value.Mode = allocation.Manifest.Mode
		value.Engineering = allocation.Engineering
		values = append(values, value)
	}
	err = rows.Err()
	rows.Close()
	if err != nil {
		return err
	}
	for index := range values {
		if values[index].Mode != "trial" {
			continue
		}
		var eligible bool
		var status, reason, version string
		var count int
		err := db.QueryRowContext(ctx, "SELECT trial_version,initial_player_count,eligible,status,reason FROM pve_dc_trial_records WHERE run_id=?", values[index].Run).Scan(&version, &count, &eligible, &status, &reason)
		if err != nil && err != sql.ErrNoRows {
			return err
		}
		if err != nil {
			values[index].Trial = map[string]any{"record_present": false}
			continue
		}
		values[index].Trial = map[string]any{"record_present": true, "version": version, "initial_player_count": count, "eligible": eligible, "status": status, "reason": reason}
		if values[index].Engineering && eligible {
			return fmt.Errorf("engineering run has eligible release record: %s", values[index].Run)
		}
	}
	var pending int
	if err := db.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_pending_operations o JOIN pve_runs r ON r.id=o.aggregate_id WHERE r.rule_version='dc_ruins.v5.p5.1' AND o.status<>'done'").Scan(&pending); err != nil {
		return err
	}
	var fixtureLeak int
	if err := db.QueryRowContext(ctx, "SELECT COUNT(DISTINCT t.record_id) FROM pve_dc_trial_records t JOIN pve_runs r ON r.id=t.run_id JOIN pve_run_participants m ON m.run_id=r.id JOIN players p ON p.id=m.player_id WHERE r.rule_version='dc_ruins.v5.p5.1' AND t.trial_version='trial.v5.1' AND t.eligible=1 AND p.password_hash='not_a_login_hash' AND p.nickname='P0 integration'").Scan(&fixtureLeak); err != nil {
		return err
	}
	if fixtureLeak != 0 {
		return fmt.Errorf("publication fixture present in release trial namespace: %d", fixtureLeak)
	}
	scope := "read_only_demo_storage_audit"
	if *quarantine {
		scope = "demo_audit_with_explicit_fixture_namespace_repair"
	}
	return json.NewEncoder(os.Stdout).Encode(map[string]any{"profile": "dc_ruins.v5.p5.1", "scope": scope, "recorded_at_utc": time.Now().UTC(), "runs": values, "pending_operations": pending, "normal_release_score_contamination": false, "fixture_namespace_repairs": repairs})
}
