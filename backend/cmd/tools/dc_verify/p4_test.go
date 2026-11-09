package main

import "testing"

func TestP4ShieldProofAcceptsUntriggeredEligibleDeadMember(test *testing.T) {
	if !p4ShieldProof(true, false, 0, "dead", 15) || !p4ShieldProof(true, true, 100, "alive", 0) {
		test.Fatal("valid shield lifecycle rejected")
	}
	if p4ShieldProof(true, false, 100, "alive", 15) || p4ShieldProof(true, false, 0, "dead", 0) || p4ShieldProof(false, false, 0, "dead", 15) {
		test.Fatal("missing or lost shield accepted")
	}
}

func TestP4LifecycleVerificationSeparatesTrialAndNormalPause(t *testing.T) {
	normal := map[string]any{"complete": true, "reconnect_choice_preserved": true, "pause_choice_frozen": true}
	trial := map[string]any{"complete": true, "reconnect_choice_preserved": true, "trial_menu_not_paused": true, "trial_choice_world_continued": true}
	if !p4LifecycleProofComplete(normal, false) || p4LifecycleProofComplete(normal, true) || !p4LifecycleProofComplete(trial, true) || p4LifecycleProofComplete(trial, false) {
		t.Fatal("trial world-continuation proof was confused with ordinary pause")
	}
	trial["trial_choice_world_continued"] = false
	if p4LifecycleProofComplete(trial, true) {
		t.Fatal("trial with stopped world accepted")
	}
}
