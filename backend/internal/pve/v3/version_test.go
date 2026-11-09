package v3

import "testing"

func TestV8AdmissionRetainsExactFrozenBoundary(t *testing.T) {
	expected := Admission{OperationVersion: "dc_ruins.v8", ProtocolVersion: 8, ContentVersion: "dc_content.v8.p5.1", WorldVersion: "dc_rooms.v8.p5.1", TrialVersion: "trial.v8.p5.1", ConfigHash: "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"}
	base := expected
	base.TrialVersion = ""
	base.Mode = "normal"
	base.InitialPlayers = 1
	base.PlayerID = "101"
	base.RunID = "run_v8_1"
	base.ConnectionGen = 1
	if err := ValidateAdmissionAgainst(base, expected); err != nil {
		t.Fatal("valid protocol8 admission rejected:", err)
	}
	for name, mutate := range map[string]func(*Admission){
		"old protocol":    func(v *Admission) { v.ProtocolVersion = 7 },
		"old operation":   func(v *Admission) { v.OperationVersion = "dc_ruins.v7" },
		"wrong world":     func(v *Admission) { v.WorldVersion = "dc_rooms.v7.p5.2" },
		"wrong hash":      func(v *Admission) { v.ConfigHash = "1111111111111111111111111111111111111111111111111111111111111111" },
		"zero generation": func(v *Admission) { v.ConnectionGen = 0 },
	} {
		value := base
		mutate(&value)
		if ValidateAdmissionAgainst(value, expected) == nil {
			t.Fatal("accepted", name)
		}
	}
	unknown := base
	unknown.ProtocolVersion = 9
	unknownExpected := expected
	unknownExpected.ProtocolVersion = 9
	if ValidateAdmissionAgainst(unknown, unknownExpected) == nil {
		t.Fatal("unregistered future protocol accepted")
	}
}

func TestValidateAdmissionRequiresFrozenVersions(t *testing.T) {
	base := Admission{OperationVersion: OperationVersion, ProtocolVersion: ProtocolVersion, ContentVersion: ContentVersion, WorldVersion: WorldVersion, ConfigHash: "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", Mode: "normal", InitialPlayers: 1, PlayerID: "101", RunID: "run-1", ConnectionGen: 1}
	if err := ValidateAdmission(base, base.ConfigHash); err != nil {
		t.Fatal(err)
	}
	for name, mutate := range map[string]func(*Admission){
		"old operation": func(value *Admission) { value.OperationVersion = "dc_ruins.v2" },
		"old protocol":  func(value *Admission) { value.ProtocolVersion = 2 },
		"wrong hash": func(value *Admission) {
			value.ConfigHash = "1111111111111111111111111111111111111111111111111111111111111111"
		},
		"trial missing version":           func(value *Admission) { value.Mode = "trial" },
		"normal trial version":            func(value *Admission) { value.TrialVersion = TrialVersion },
		"zero player":                     func(value *Admission) { value.InitialPlayers = 0 },
		"malformed admission hash":        func(value *Admission) { value.ConfigHash = "not-a-hash" },
		"malformed player id":             func(value *Admission) { value.PlayerID = "player-101" },
		"zero player id":                  func(value *Admission) { value.PlayerID = "0" },
		"leading zero player id":          func(value *Admission) { value.PlayerID = "0101" },
		"overflow player id":              func(value *Admission) { value.PlayerID = "9223372036854775808" },
		"malformed run id":                func(value *Admission) { value.RunID = "run/1" },
		"oversized connection generation": func(value *Admission) { value.ConnectionGen = 4294967296 },
	} {
		t.Run(name, func(t *testing.T) {
			value := base
			mutate(&value)
			if err := ValidateAdmission(value, base.ConfigHash); err == nil {
				t.Fatal("invalid v3 admission accepted")
			}
		})
	}
}

func TestValidateAdmissionRejectsMalformedExpectedHash(t *testing.T) {
	base := Admission{OperationVersion: OperationVersion, ProtocolVersion: ProtocolVersion, ContentVersion: ContentVersion, WorldVersion: WorldVersion, ConfigHash: "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", Mode: "normal", InitialPlayers: 1, PlayerID: "101", RunID: "run-1", ConnectionGen: 1}
	if err := ValidateAdmission(base, "not-a-hash"); err != ErrVersionMismatch {
		t.Fatalf("expected malformed expected hash to be rejected, got %v", err)
	}
}

func TestValidateConfigHash(t *testing.T) {
	if err := ValidateConfigHash("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"); err != nil {
		t.Fatal(err)
	}
	for _, value := range []string{"", "abc", "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz"} {
		if err := ValidateConfigHash(value); err == nil {
			t.Fatalf("invalid hash accepted: %q", value)
		}
	}
}
