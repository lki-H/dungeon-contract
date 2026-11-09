package gameplay

import (
	"game-realtime-gm/backend/internal/pve/store"
	"game-realtime-gm/backend/internal/pve/v3"
	"path/filepath"
	"testing"
)

func TestV6FrozenStagesAndLayouts(t *testing.T) {
	c, e := LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v6-p5.json"))
	if e != nil {
		t.Fatal(e)
	}
	s := &Service{Content: c}
	if !s.P5() || !s.P3() || !s.S1() || c.ProtocolVersion() != 6 {
		t.Fatal("stage chain")
	}
	if c.Rules.Version != "dc_ruins.v6.p5.2" || len(c.Rules.Tasks) != 6 {
		t.Fatal("identity")
	}
	for _, task := range c.Rules.Tasks {
		if task.Version != "dc_tasks.v5.p4.1" {
			t.Fatal("task semantic change")
		}
	}
	for _, seed := range []int64{1, 17, 20261012, 99} {
		w, h, e := c.P4World(seed)
		if e != nil || len(h) != 64 {
			t.Fatal(e)
		}
		rooms := w["rooms"].([]any)
		if len(rooms) != 15 {
			t.Fatal("zones")
		}
		for _, r := range rooms {
			room := r.(map[string]any)
			if room["variants"] != nil || room["variant_id"] == nil {
				t.Fatal("unfrozen variant")
			}
			if len(room["cover_points_milli"].([]any)) != len(room["covers"].([]any)) {
				t.Fatal("cover selection")
			}
		}
	}
	good := v3.Admission{OperationVersion: c.V3.Operation, ProtocolVersion: 6, ContentVersion: c.V3.Content, WorldVersion: c.V3.World, ConfigHash: c.Hash, Mode: "normal", InitialPlayers: 4, PlayerID: "101", RunID: "run_1", ConnectionGen: 1}
	expected := good
	if e = v3.ValidateAdmissionAgainst(good, expected); e != nil {
		t.Fatal(e)
	}
	good.ProtocolVersion = 5
	if v3.ValidateAdmissionAgainst(good, expected) == nil {
		t.Fatal("accepted old wire protocol")
	}
	value, e := DecodeJSON(store.JSON(map[string]any{"instance_id": "dc_local_01", "boot_id": "11111111-1111-4111-8111-111111111111", "protocol_version": 6, "versions": c.V3, "config_versions": []ConfigVersion{{ID: c.Rules.Version, Hash: c.Hash}}}))
	if e != nil {
		t.Fatal(e)
	}
	schema := c.OpenAPI["components"].(map[string]any)["schemas"].(map[string]any)["RegisterRequest"].(map[string]any)
	if e = ValidateSchema(value, schema); e != nil {
		t.Fatal(e)
	}
}
