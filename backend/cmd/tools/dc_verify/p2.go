package main

import (
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"game-realtime-gm/backend/internal/pve/store"
	"github.com/gorilla/websocket"
	"net/http"
	"os"
	"path/filepath"
	"time"
)

func p2WorldEvidenceName(index, count int) string {
	if count == 1 {
		return "client-p1-v3.json"
	}
	return fmt.Sprintf("client-p1-v3-%d.json", index)
}

func (runner *harness) verifyP2Multiplayer(db *sql.DB, count int, pressure bool) error {
	deadline := time.Now().Add(65 * time.Second)
	if pressure {
		deadline = time.Now().Add(105 * time.Second)
	}
	verified := make([]bool, count)
	for time.Now().Before(deadline) {
		complete := true
		for index := 0; index < count; index++ {
			if verified[index] {
				continue
			}
			data, err := os.ReadFile(filepath.Join(runner.output, fmt.Sprintf("client-p2-%d.json", index)))
			if err != nil {
				complete = false
				continue
			}
			var proof struct {
				Run           string `json:"run_id"`
				Ack           int64  `json:"input_ack"`
				Dynamic       int    `json:"dynamic_packets"`
				DynamicBytes  int    `json:"max_dynamic_bytes"`
				BaselineBytes int    `json:"max_baseline_bytes"`
				Separated     bool   `json:"separate_rooms_observed"`
				Continued     bool   `json:"world_continued"`
				Reconnected   bool   `json:"reconnect_generation_changed"`
				CombatFrames  int    `json:"combat_observed_frames"`
			}
			if json.Unmarshal(data, &proof) != nil || proof.Run != runner.runID || proof.Ack < 1 || proof.Dynamic < 1 || proof.DynamicBytes > 900 || proof.BaselineBytes < 66 || proof.BaselineBytes > 900 || !proof.Separated || !proof.Continued || index == 0 && !proof.Reconnected {
				return errors.New("P2 multiplayer network proof did not satisfy requirements")
			}
			if pressure && proof.CombatFrames < 30 {
				return errors.New("Pressure client has insufficient actual combat samples")
			}
			if pressure && index > 0 && proof.Reconnected {
				return errors.New("Pressure client had an unrequested reconnect; retain evidence and investigate")
			}
			verified[index] = true
		}
		if complete {
			break
		}
		time.Sleep(200 * time.Millisecond)
	}
	for _, value := range verified {
		if !value {
			return errors.New("P2 multiplayer network evidence timed out")
		}
	}
	for _, account := range runner.clients {
		account.socket.Close()
		header := http.Header{}
		header.Set("Authorization", "Bearer "+account.token)
		socket, _, err := websocket.DefaultDialer.DialContext(runner.ctx, "ws://127.0.0.1:12880/ws", header)
		if err != nil {
			return err
		}
		account.socket = socket
		if _, err := runner.command(account, "v2.run.leave", map[string]any{"run_id": runner.runID}); err != nil {
			return err
		}
	}
	for attempt := 0; attempt < 100; attempt++ {
		var status string
		if err := db.QueryRowContext(runner.ctx, "SELECT status FROM pve_runs WHERE id=?", runner.runID).Scan(&status); err != nil {
			return err
		}
		if status == "closed" {
			var rewards int
			if err := db.QueryRowContext(runner.ctx, "SELECT COUNT(*) FROM pve_reward_grants WHERE run_id=?", runner.runID).Scan(&rewards); err != nil || rewards != 0 {
				return errors.New("Multiplayer stage created rewards")
			}
			summary := map[string]any{"run_id": runner.runID, "real_udp_clients": count, "individual_room_interest": true, "actual_reconnect": true, "input_ack": true, "dynamic_and_baseline_packet_budget": true, "reliable_close": true, "rewards": false, "human": false}
			summary["combat_pressure"] = pressure
			fmt.Println(string(store.JSON(summary)))
			return os.WriteFile(filepath.Join(runner.output, "summary.json"), store.JSON(summary), 0600)
		}
		time.Sleep(100 * time.Millisecond)
	}
	return errors.New("P2 multiplayer reliable close did not complete")
}

func (runner *harness) verifyP2Full(db *sql.DB, loadout, scenario string) error {
	ticker := time.NewTicker(time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-runner.ctx.Done():
			return errors.New("P2 full flow timeout")
		case <-ticker.C:
			var status string
			if err := db.QueryRowContext(runner.ctx, "SELECT status FROM pve_runs WHERE id=?", runner.runID).Scan(&status); err != nil {
				return err
			}
			if status != "closed" {
				continue
			}
			var raw []byte
			if err := db.QueryRowContext(runner.ctx, "SELECT JSON_EXTRACT(state,'$.p1_stage_result') FROM pve_dc_allocations WHERE run_id=?", runner.runID).Scan(&raw); err != nil {
				return err
			}
			var result struct {
				Outcome   string `json:"outcome"`
				Completed []bool `json:"completed_rooms"`
				Elapsed   int64  `json:"elapsed_ms"`
			}
			if json.Unmarshal(raw, &result) != nil {
				return errors.New("Full flow result missing")
			}
			expected := "success"
			if scenario == "wipe" {
				expected = "p1_incapacitated"
			}
			if result.Outcome != expected {
				_ = os.WriteFile(filepath.Join(runner.output, "failed-full-result.json"), store.JSON(map[string]any{"run_id": runner.runID, "outcome": result.Outcome, "completed_rooms": result.Completed, "elapsed_ms": result.Elapsed, "real_udp": true, "human": false}), 0600)
				return fmt.Errorf("Full input-only run ended %s; expected %s", result.Outcome, expected)
			}
			var rewards int
			if err := db.QueryRowContext(runner.ctx, "SELECT COUNT(*) FROM pve_reward_grants WHERE run_id=?", runner.runID).Scan(&rewards); err != nil || rewards != 0 {
				return errors.New("Full flow stage rewards")
			}
			if len(runner.processes) > 0 {
				for attempt := 0; attempt < 50; attempt++ {
					if _, err := os.Stat(filepath.Join(runner.output, "client-full-metrics.json")); err == nil {
						break
					}
					time.Sleep(100 * time.Millisecond)
				}
			}
			return os.WriteFile(filepath.Join(runner.output, "summary.json"), store.JSON(map[string]any{"run_id": runner.runID, "loadout": loadout, "outcome": result.Outcome, "completed_rooms": result.Completed, "elapsed_ms": result.Elapsed, "real_udp": true, "input_only": true, "modified_config": false, "rewards": false, "human": false}), 0600)
		}
	}
}
