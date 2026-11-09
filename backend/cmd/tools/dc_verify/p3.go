package main

import (
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"time"

	"game-realtime-gm/backend/internal/pve/store"
	"github.com/gorilla/websocket"
)

func (runner *harness) verifyP3(db *sql.DB, loadout, scenario string, full bool, count int, pressure bool) error {
	leaving := false
	rescueVerified := false
	for {
		select {
		case <-runner.ctx.Done():
			return errors.New("P3 actual process verification timed out")
		default:
		}
		var status, reason string
		var frames, rescues, downed int64
		if err := db.QueryRowContext(runner.ctx, "SELECT status,COALESCE(end_reason,'') FROM pve_runs WHERE id=?", runner.runID).Scan(&status, &reason); err != nil {
			return err
		}
		if err := db.QueryRowContext(runner.ctx, "SELECT COUNT(*),COALESCE(SUM(event_type='rescued'),0),COALESCE(SUM(event_type='downed'),0) FROM pve_run_events WHERE run_id=?", runner.runID).Scan(&frames, &rescues, &downed); err != nil {
			return err
		}
		if !full && !leaving && status == "running" {
			ready := true
			for index := 0; index < count; index++ {
				data, err := os.ReadFile(filepath.Join(runner.output, p2WorldEvidenceName(index, count)))
				var proof struct {
					Ack   int64 `json:"input_ack"`
					Frame int64 `json:"world_frame_id"`
				}
				if err != nil || json.Unmarshal(data, &proof) != nil || proof.Ack < 1 || proof.Frame < 30 {
					ready = false
				}
			}
			if count == 1 && scenario != "rescue" {
				data, err := os.ReadFile(filepath.Join(runner.output, "client-s2-probe.json"))
				if err != nil {
					ready = false
				} else {
					var probe map[string]any
					if json.Unmarshal(data, &probe) != nil {
						return errors.New("P3 action probe malformed")
					}
					keys := []string{"single_weapon_switch", "manual_charge_release", "primary_from_melee", "secondary_from_melee", "pause_new_skills", "resume_new_skills", "post_resume_action"}
					if filepath.Base(os.Getenv("DC_CONFIG_PATH")) == "demo-v5-p4.json" || (filepath.Base(os.Getenv("DC_CONFIG_PATH")) == "demo-v5-p5.json" || modernRuntimeConfigured()) {
						keys = []string{"single_weapon_switch", "melee_current_weapon", "rune_charge_or_blade_no_charge", "primary_from_melee", "secondary_from_melee", "pause_new_skills", "resume_new_skills", "post_resume_action"}
						if runner.trial {
							keys = []string{"single_weapon_switch", "melee_current_weapon", "rune_charge_or_blade_no_charge", "primary_from_melee", "secondary_from_melee", "trial_menu_not_paused", "trial_world_continued", "trial_menu_cancels_continuous_input", "post_menu_action"}
						}
					}
					for _, key := range keys {
						if probe[key] != true {
							return fmt.Errorf("P3 actual action probe failed %s", key)
						}
					}
				}
			}
			if count > 1 && scenario != "rescue" {
				for index := 0; index < count; index++ {
					data, err := os.ReadFile(filepath.Join(runner.output, fmt.Sprintf("client-p2-%d.json", index)))
					if err != nil {
						ready = false
						continue
					}
					var probe struct {
						Separated   bool `json:"separate_rooms_observed"`
						Continued   bool `json:"world_continued"`
						Reconnected bool `json:"reconnect_generation_changed"`
						Dynamic     int  `json:"max_dynamic_bytes"`
						Baseline    int  `json:"max_baseline_bytes"`
						Combat      int  `json:"combat_observed_frames"`
						Ordinary    bool `json:"ordinary_attack_episode_observed"`
					}
					if json.Unmarshal(data, &probe) != nil || !probe.Separated || !probe.Continued || index == 0 && !probe.Reconnected || index > 0 && probe.Reconnected || probe.Dynamic > 900 || probe.Baseline > 900 || pressure && (probe.Combat < 30 || filepath.Base(os.Getenv("DC_CONFIG_PATH")) == "demo-v8-p5.json" && !probe.Ordinary) {
						return errors.New("P3 multiplayer input/reconnect proof failed")
					}
				}
			}
			if scenario == "rescue" {
				ready = ready && rescues > 0 && downed > 0
				rescueVerified = rescues > 0 && downed > 0
			}
			if ready {
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
				leaving = true
			}
		}
		if status == "closed" {
			if !full && !leaving {
				return fmt.Errorf("P3 process closed before verified input and public leave: authority outcome=%s", reason)
			}
			if full && ((scenario == "success" && reason != "success") || (scenario == "wipe" && reason != "team_wipe")) {
				return fmt.Errorf("P3 actual full run ended %s", reason)
			}
			if scenario == "rescue" && !rescueVerified {
				return errors.New("P3 run ended before real rescue")
			}
			var rewards, ledger, statistics int64
			if err := db.QueryRowContext(runner.ctx, "SELECT COALESCE(SUM(amount),0) FROM pve_reward_grants WHERE run_id=?", runner.runID).Scan(&rewards); err != nil {
				return err
			}
			if err := db.QueryRowContext(runner.ctx, "SELECT COUNT(*) FROM asset_ledger WHERE pve_run_id=?", runner.runID).Scan(&ledger); err != nil {
				return err
			}
			if err := db.QueryRowContext(runner.ctx, "SELECT COUNT(*) FROM pve_dc_v4_life_stats WHERE run_id=?", runner.runID).Scan(&statistics); err != nil {
				return err
			}
			if full && (frames < 3 || statistics != int64(count) || scenario == "success" && (rewards < 100 || ledger < 1)) {
				return errors.New("P3 final facts/statistics/assets missing")
			}
			if full && scenario == "wipe" {
				var used int
				if err := db.QueryRowContext(runner.ctx, "SELECT reinforcement_used FROM pve_runs WHERE id=?", runner.runID).Scan(&used); err != nil {
					return err
				}
				if used != 3 || downed < int64(count+3) {
					return errors.New("P3 failure did not exhaust actual finite recovery")
				}
			}
			for index, account := range runner.clients {
				request, err := http.NewRequestWithContext(runner.ctx, "GET", "http://127.0.0.1:12880/api/v2/runs/"+runner.runID+"/results", nil)
				if err != nil {
					return err
				}
				request.Header.Set("Authorization", "Bearer "+account.token)
				response, err := runner.http.Do(request)
				if err != nil {
					return err
				}
				data, readErr := io.ReadAll(io.LimitReader(response.Body, 65536))
				response.Body.Close()
				if readErr != nil || response.StatusCode != 200 {
					return errors.New("P3 authenticated result query failed")
				}
				var result struct {
					Data map[string]any `json:"data"`
				}
				if json.Unmarshal(data, &result) != nil {
					return errors.New("P3 result shape")
				}
				if filepath.Base(os.Getenv("DC_CONFIG_PATH")) == "demo-v5-p5.json" || modernRuntimeConfigured() {
					elapsed, valid := result.Data["elapsed_ms"].(float64)
					if !valid || elapsed <= 0 {
						return errors.New("P5 reliable result omitted authority elapsed time")
					}
				}
				if err := os.WriteFile(filepath.Join(runner.output, fmt.Sprintf("public-result-%d.json", index)), data, 0600); err != nil {
					return err
				}
			}
			time.Sleep(1500 * time.Millisecond)
			return os.WriteFile(filepath.Join(runner.output, "summary.json"), store.JSON(map[string]any{"stage": "P3", "run_id": runner.runID, "loadout": loadout, "real_udp_clients": count, "real_storage": true, "input_only": true, "modified_world_state": false, "scenario": scenario, "pressure": pressure, "outcome": reason, "real_rescues": rescues, "real_knockdowns": downed, "reliable_facts": frames, "reward_total": rewards, "ledger_entries": ledger, "life_stat_rows": statistics, "human": false, "cross_pc": false}), 0600)
		}
		time.Sleep(200 * time.Millisecond)
	}
}
