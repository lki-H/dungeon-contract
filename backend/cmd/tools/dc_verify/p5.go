package main

import (
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"game-realtime-gm/backend/internal/pve/store"
	"os"
	"path/filepath"
	"time"
)

func (runner *harness) verifyP5Lobby(db *sql.DB) error {
	deadline := time.Now().Add(110 * time.Second)
	proofPath := filepath.Join(runner.output, "p5-lobby.json")
	for time.Now().Before(deadline) {
		bytes, err := os.ReadFile(proofPath)
		var proof struct {
			Status      string   `json:"status"`
			Runs        []string `json:"runs"`
			Generations []int64  `json:"source_generations"`
			Assignments []string `json:"assignment_ids"`
			Loadouts    []string `json:"loadouts"`
		}
		if err == nil && json.Unmarshal(bytes, &proof) == nil && proof.Status == "PASS" {
			if len(proof.Runs) != 2 || len(proof.Generations) != 2 || len(proof.Assignments) != 2 || len(proof.Loadouts) != 2 || proof.Runs[0] == proof.Runs[1] || proof.Assignments[0] == proof.Assignments[1] || proof.Generations[0] < 1 || proof.Generations[1] < 1 || proof.Loadouts[0] != "ranger" || proof.Loadouts[1] != "runesmith" {
				return errors.New("P5 lobby did not prove independent consecutive assignments")
			}
			allClosed := true
			for _, run := range proof.Runs {
				var state string
				var pending, rewards int
				if err := db.QueryRowContext(runner.ctx, "SELECT status FROM pve_runs WHERE id=?", run).Scan(&state); err != nil {
					return err
				}
				if state != "closed" {
					allClosed = false
					break
				}
				if err := db.QueryRowContext(runner.ctx, "SELECT COUNT(*) FROM pve_pending_operations WHERE aggregate_id=? AND status<>'done'", run).Scan(&pending); err != nil {
					return err
				}
				if err := db.QueryRowContext(runner.ctx, "SELECT COALESCE(SUM(amount),0) FROM pve_reward_grants WHERE run_id=?", run).Scan(&rewards); err != nil {
					return err
				}
				if pending != 0 || rewards != 0 {
					return errors.New("bounded lobby leave left pending operations or fabricated rewards")
				}
			}
			if !allClosed {
				time.Sleep(200 * time.Millisecond)
				continue
			}
			var idle int
			if err := db.QueryRowContext(runner.ctx, "SELECT COUNT(*) FROM pve_dc_instances WHERE status='idle' AND current_assignment_id IS NULL AND lease_until>UTC_TIMESTAMP(3)").Scan(&idle); err != nil {
				return err
			}
			if idle != 1 {
				time.Sleep(200 * time.Millisecond)
				continue
			}
			runner.runID = proof.Runs[1]
			return os.WriteFile(filepath.Join(runner.output, "summary.json"), store.JSON(map[string]any{"profile": map[bool]string{true: modernProfile(), false: "dc_ruins.v5.p5.1"}[modernRuntimeConfigured()], "scenario": "p5_lobby_two_runs", "status": "ENGINEERING_WORKFLOW_PASS", "run_ids": proof.Runs, "assignment_ids": proof.Assignments, "loadouts": proof.Loadouts, "source_generations": proof.Generations, "identity_scope": "run_id + assignment_id + source_generation; source generation may remain stable under the same boot", "human": false, "cross_pc": false, "world_state_modified": false, "real_storage": true, "real_udp_clients": 1, "authority_rotated": true, "pending_operations": 0, "reward_total": 0}), 0600)
		}
		time.Sleep(200 * time.Millisecond)
	}
	return errors.New("P5 Unity lobby workflow did not complete; inspect retained client/service logs")
}
func (runner *harness) waitP5ResultCaptures(players int) error {
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		ready := true
		for index := 0; index < players; index++ {
			info, err := os.Stat(filepath.Join(runner.output, fmt.Sprintf("result-%d.png", index)))
			if err != nil || info.Size() < 1000 {
				ready = false
			}
		}
		if ready {
			return nil
		}
		time.Sleep(100 * time.Millisecond)
	}
	return errors.New("reliable gameplay result confirmed but P5 rendered contribution cards were not captured")
}
