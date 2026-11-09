package main

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"

	"game-realtime-gm/backend/internal/pve/store"
)

// Reliable lifecycle sequences must remain contiguous even when UDP is conditioned.
// A gameplay victory alone does not validate the client replication path.
type v6SyncProof struct {
	RunID        string `json:"run_id"`
	Protocol     int    `json:"protocol_version"`
	Ack          int64  `json:"input_ack"`
	Spawns       *int   `json:"lifecycle_spawns"`
	Terminations *int   `json:"lifecycle_terminations"`
	Gaps         *int   `json:"lifecycle_gaps"`
	Pending      *int   `json:"lifecycle_pending_unknown"`
	Packet       int    `json:"max_dynamic_bytes"`
}

func (proof v6SyncProof) validate(run string, protocols ...int) error {
	protocol := 6
	if len(protocols) > 0 {
		protocol = protocols[0]
	}
	if proof.RunID != run || proof.Protocol != protocol || proof.Ack < 1 || proof.Packet < 1 || proof.Packet > 900 || proof.Spawns == nil || proof.Terminations == nil || proof.Gaps == nil || proof.Pending == nil {
		return errors.New("missing or mismatched v6 synchronization evidence")
	}
	if *proof.Spawns < 1 || *proof.Terminations < 1 || *proof.Gaps != 0 || *proof.Pending != 0 {
		return fmt.Errorf("v6 lifecycle quality: spawns=%d terminations=%d gaps=%d pending=%d", *proof.Spawns, *proof.Terminations, *proof.Gaps, *proof.Pending)
	}
	return nil
}

func (runner *harness) verifyV6Synchronization(count int) error {
	results := make([]map[string]any, 0, count)
	valid := true
	for index := 0; index < count; index++ {
		name := p2WorldEvidenceName(index, count)
		data, readErr := os.ReadFile(filepath.Join(runner.output, name))
		var proof v6SyncProof
		err := readErr
		if err == nil {
			err = json.Unmarshal(data, &proof)
		}
		if err == nil {
			err = proof.validate(runner.runID, modernProtocol())
		}
		row := map[string]any{"slot": index, "file": name, "proof": proof, "valid": err == nil}
		if err != nil {
			valid = false
			row["reason"] = err.Error()
		}
		results = append(results, row)
	}
	status := "PASS"
	if !valid {
		status = "FAIL_SYNCHRONIZATION"
	}
	report := map[string]any{"profile": modernProfile(), "run_id": runner.runID, "status": status, "scope": "actual_client_reliable_lifecycle", "human": false, "cross_pc": false, "clients": results}
	if err := os.WriteFile(filepath.Join(runner.output, "synchronization-quality.json"), store.JSON(report), 0600); err != nil {
		return err
	}
	if !valid {
		return errors.New("gameplay result retained; v6 client lifecycle synchronization failed (see synchronization-quality.json)")
	}
	return nil
}
