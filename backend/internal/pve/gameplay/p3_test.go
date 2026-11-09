package gameplay

import (
	"encoding/json"
	"path/filepath"
	"testing"
)

func TestP3ProfileAndVersionIsolation(test *testing.T) {
	content, err := LoadContent(filepath.Join(filepath.Dir(contentPath()), "demo-v4-p3.json"))
	if err != nil {
		test.Fatal(err)
	}
	previous, err := LoadContent(filepath.Join(filepath.Dir(contentPath()), "demo-v4-p2-mp.json"))
	if err != nil {
		test.Fatal(err)
	}
	service := &Service{Content: content}
	if !service.P3() || !service.S1() || service.StagePlayerLimit() != 4 || content.Hash == previous.Hash || len(content.Rules.Tasks) != 6 || content.Rules.Reinforcements != 3 {
		test.Fatal("P3 profile isolation")
	}
	survivor, _ := content.Rules.Task("dc_survivor")
	if !survivor.NoKnockdowns || survivor.NoDeaths || survivor.Immediate() {
		test.Fatal("survival semantic version")
	}
	if content.WorldHash == previous.WorldHash {
		test.Fatal("business world binding not distinct")
	}
}

func TestP3ActualUnityDamageFrame(test *testing.T) {
	content, err := LoadContent(filepath.Join(filepath.Dir(contentPath()), "demo-v4-p3.json"))
	if err != nil {
		test.Fatal(err)
	}
	data := []byte(`{"schema_version":4,"source":"unity","run_id":"run_test","instance_id":"dc_local_01","boot_id":"25144428-c3b2-71bb-9ab8-c6ed6213e1bd","assignment_id":"assign_test","source_generation":513,"sequence":8,"frame_id":"3dbef6a0-6a33-408d-83bf-708fe038800b","world_frame_id":207,"elapsed_ms":6896,"wall_elapsed_ms":6896,"manual_pause_ms":0,"control_version":0,"facts":[{"event_id":"526926fd-d745-4b0f-b45a-57f8a14cf0b1","kind":"damage","actor_player_id":"759","target_id":"guard_44","action_id":"shot_52","effective_amount":12,"contributors":[],"proof":{"origin_life_generation":1,"emission_frame":204},"life_generation":1,"downed_generation":0,"event_game_ms":6896}],"terminal":null,"summary":null,"fingerprint":"018213034cec6aa472cc7839250a683cc878540a809d7574208076456f7a2c8c"}`)
	value, err := DecodeJSON(data)
	if err != nil {
		test.Fatal(err)
	}
	if err := ValidateSchema(value, content.FrameSchema); err != nil {
		test.Fatal("Unity damage shape", err)
	}
	var frame Frame
	if err := json.Unmarshal(data, &frame); err != nil {
		test.Fatal(err)
	}
	if err := validateP3Frame(frame); err != nil {
		test.Fatal(err)
	}
}
