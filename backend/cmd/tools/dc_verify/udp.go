package main

import (
	"errors"
	"os"
	"path/filepath"

	"game-realtime-gm/backend/internal/dctestnet"
	"game-realtime-gm/backend/internal/pve/store"
)

func (runner *harness) finishUDP() error {
	if len(runner.udpRelays) == 0 {
		return nil
	}
	results := make([]dctestnet.Snapshot, 0, len(runner.udpRelays))
	valid := true
	for _, relay := range runner.udpRelays {
		_ = relay.Close()
		s := relay.Stats()
		results = append(results, s)
		valid = valid && s.Upstream > 0 && s.Downstream > 0 && s.IntentionalLoss > 0 && s.BudgetDrops == 0 && s.Oversized == 0 && s.WriteErrors == 0
	}
	report := map[string]any{"profile": modernProfile(), "status": "COMPLETE_UDP_CONDITIONED_PATH", "conditioner_profile": runner.udpProfile, "real_udp": true, "input_only": true, "modified_world_state": false, "loopback": true, "cross_pc": false, "human": false, "tcp_control_conditioned": false, "datagrams_recorded": false, "transport_counters_valid": valid, "results": results}
	if !valid {
		report["status"] = "FAIL_CONDITIONER_COUNTERS"
	}
	if err := os.WriteFile(filepath.Join(runner.output, "udp-conditioner.json"), store.JSON(report), 0600); err != nil {
		return err
	}
	if !valid {
		return errors.New("UDP conditioner counters show missing traffic or unexpected resource/write loss")
	}
	return nil
}
