package main

import "testing"

func TestV6SyncQualityRejectsVictoryWithLifecycleGap(t *testing.T) {
	zero, one, gaps := 0, 1, 236
	proof := v6SyncProof{RunID: "run_test", Protocol: 6, Ack: 1, Spawns: &one, Terminations: &one, Gaps: &zero, Pending: &zero, Packet: 815}
	if err := proof.validate("run_test"); err != nil {
		t.Fatal(err)
	}
	proof.Gaps = &gaps
	if proof.validate("run_test") == nil {
		t.Fatal("a real victory with persistent lifecycle gaps was accepted")
	}
	proof.Gaps, proof.Pending = &zero, &one
	if proof.validate("run_test") == nil {
		t.Fatal("unresolved unknown projectile accepted")
	}
	proof.Pending, proof.Spawns = &zero, nil
	if proof.validate("run_test") == nil {
		t.Fatal("absent counter accepted as zero")
	}
	proof.Spawns = &one
	if proof.validate("different_run") == nil {
		t.Fatal("old run evidence accepted")
	}
}
