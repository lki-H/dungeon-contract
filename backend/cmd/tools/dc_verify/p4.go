package main

import (
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"

	"game-realtime-gm/backend/internal/pve/store"
)

func (runner *harness) verifyP4(db *sql.DB, loadout, scenario string, full bool, count int, pressure bool) error {
	accepted := false
	defer func() {
		if !accepted {
			_ = os.Rename(filepath.Join(runner.output, "summary.json"), filepath.Join(runner.output, "common-checks-partial.json"))
		}
	}()
	if err := runner.verifyP3(db, loadout, scenario, full, count, pressure); err != nil {
		return err
	}
	if modernRuntimeConfigured() {
		if err := runner.verifyV6Synchronization(count); err != nil {
			return err
		}
	}
	var mode string
	if err := db.QueryRowContext(runner.ctx, "SELECT JSON_UNQUOTE(JSON_EXTRACT(manifest,'$.mode')) FROM pve_dc_allocations WHERE run_id=?", runner.runID).Scan(&mode); err != nil {
		return err
	}
	if mode != "normal" && mode != "trial" || (mode == "trial") != runner.trial {
		return errors.New("actual Run mode does not match the requested verification mode")
	}
	partyLoadouts := []string{}
	for index := 0; index < count; index++ {
		data, err := os.ReadFile(filepath.Join(runner.output, fmt.Sprintf("client-p4-%d.json", index)))
		var proof map[string]any
		if err != nil || json.Unmarshal(data, &proof) != nil {
			return errors.New("P4 per-player packet proof missing")
		}
		if visible, exists := proof["manifest_other_task_details_visible"]; !exists || visible != false {
			return errors.New("P4 private teammate task data in battle manifest")
		}
		if actual, valid := proof["loadout_id"].(string); valid {
			partyLoadouts = append(partyLoadouts, actual)
		}
	}
	var facts int
	if err := db.QueryRowContext(runner.ctx, "SELECT COUNT(*) FROM pve_run_events WHERE run_id=? AND event_type='growth_choice'", runner.runID).Scan(&facts); err != nil {
		return err
	}
	if full && scenario == "success" {
		for index := 0; index < count; index++ {
			data, err := os.ReadFile(filepath.Join(runner.output, fmt.Sprintf("client-p4-%d.json", index)))
			var proof struct {
				Rooms []struct {
					Index  int    `json:"index"`
					Exists bool   `json:"exists"`
					Status string `json:"status"`
				} `json:"rooms"`
				Pending          bool   `json:"pending_shield_observed"`
				Triggered        bool   `json:"shield_trigger_observed"`
				PendingRemaining int    `json:"pending_shield_remaining"`
				Health           int    `json:"health"`
				Life             string `json:"life_state"`
			}
			if err != nil || json.Unmarshal(data, &proof) != nil {
				return errors.New("P4 branch proof missing")
			}
			for _, room := range proof.Rooms {
				if (room.Index == 6 || room.Index == 7) && room.Exists && room.Status != "cleared" {
					return errors.New("P4 actual optional branch not completed")
				}
				if room.Index == 6 && room.Exists && !p4ShieldProof(proof.Pending, proof.Triggered, proof.Health, proof.Life, proof.PendingRemaining) {
					return errors.New("P4 pending shield lifecycle missing")
				}
			}
		}
		if facts != count*3 {
			return fmt.Errorf("P4 missing three real choices per player: %d", facts)
		}
		rows, err := db.QueryContext(runner.ctx, "SELECT choices FROM pve_dc_participant_stats WHERE run_id=?", runner.runID)
		if err != nil {
			return err
		}
		defer rows.Close()
		for rows.Next() {
			var data []byte
			var choices []string
			if err = rows.Scan(&data); err != nil {
				return err
			}
			if json.Unmarshal(data, &choices) != nil || len(choices) != 3 {
				return errors.New("P4 final growth mismatch")
			}
		}
		if err = rows.Err(); err != nil {
			return err
		}
		if count == 1 {
			data, readErr := os.ReadFile(filepath.Join(runner.output, "client-p4-0.json"))
			var proof struct {
				Probe map[string]any `json:"lifecycle_probe"`
			}
			if readErr != nil || json.Unmarshal(data, &proof) != nil || !p4LifecycleProofComplete(proof.Probe, runner.trial) {
				return errors.New("P4 pending choice pause/reconnect proof missing")
			}
			var defaults int
			if err := db.QueryRowContext(runner.ctx, "SELECT COUNT(*) FROM pve_run_events WHERE run_id=? AND event_type='growth_choice' AND JSON_EXTRACT(payload,'$.proof.automatic')=true", runner.runID).Scan(&defaults); err != nil {
				return err
			}
			if defaults < 1 {
				return errors.New("P4 automatic deadline not actually reached")
			}
		}
	}
	path := filepath.Join(runner.output, "summary.json")
	data, err := os.ReadFile(path)
	if err != nil {
		return err
	}
	var summary map[string]any
	if json.Unmarshal(data, &summary) != nil {
		return errors.New("P4 summary invalid")
	}
	summary["stage"], summary["growth_facts"], summary["human_pacing_validated"] = "P4", facts, false
	if filepath.Base(os.Getenv("DC_CONFIG_PATH")) == "demo-v5-p4.json" {
		summary["stage"] = "v5_P2.2_through_P4"
		summary["protocol_version"] = 5
		summary["profile"] = "dc_ruins.v5.p4.1"
	}
	if filepath.Base(os.Getenv("DC_CONFIG_PATH")) == "demo-v5-p5.json" || modernRuntimeConfigured() {
		summary["stage"] = "P5_engineering"
		summary["protocol_version"] = 5
		summary["profile"] = "dc_ruins.v5.p5.1"
		if modernRuntimeConfigured() {
			summary["profile"] = modernProfile()
			summary["protocol_version"] = modernProtocol()
			summary["stage"] = fmt.Sprintf("v%d_P5_engineering", modernProtocol())
		}
		summary["presentation_version"] = "p5.1"
	}
	summary["status"] = "ENGINEERING_SCENARIO_PASS"
	summary["mode"] = mode
	summary["rendered_clients"] = runner.renderClients
	summary["headless_clients"] = count - runner.renderClients
	summary["four_window_acceptance"] = count == 4 && runner.renderClients == 4
	summary["requested_full_flow"] = full
	summary["full_success"] = full && summary["outcome"] == "success"
	if len(partyLoadouts) == count {
		summary["party_loadouts"] = partyLoadouts
		summary["loadout_field_scope"] = "requested harness argument; party_loadouts records actual slots"
	}
	if err := os.WriteFile(path, store.JSON(summary), 0600); err != nil {
		return err
	}
	accepted = true
	return nil
}

func p4ShieldProof(observed, triggered bool, health int, life string, remaining int) bool {
	return observed && (triggered || health <= 0 && (life == "dead" || life == "downed" || life == "awaiting_respawn") && remaining == 15)
}

func p4LifecycleProofComplete(proof map[string]any, trial bool) bool {
	if proof["complete"] != true || proof["reconnect_choice_preserved"] != true {
		return false
	}
	if trial {
		return proof["trial_menu_not_paused"] == true && proof["trial_choice_world_continued"] == true
	}
	return proof["pause_choice_frozen"] == true
}
