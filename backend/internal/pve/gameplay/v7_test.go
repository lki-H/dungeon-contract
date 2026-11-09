package gameplay

import (
	"game-realtime-gm/backend/internal/pve/store"
	"path/filepath"
	"testing"
)

func TestV7FrozenContentAndIdentity(t *testing.T) {
	c, err := LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v7-p5.json"))
	if err != nil {
		t.Fatal(err)
	}
	if c.ProtocolVersion() != 7 || c.Rules.Version != "dc_ruins.v7.p5.2" || len(c.Rules.Tasks) != 6 {
		t.Fatal("version/task boundary")
	}
	if c.FrameSchema["$id"] != "urn:dc:frame:7:p5.2" || c.OpenAPI["info"].(map[string]any)["version"] != "7.p5.2" {
		t.Fatal("v7 contract identity still names an inherited release")
	}
	p := c.World["parameters"].(map[string]any)
	bodies := p["body_profiles"].(map[string]any)
	if len(bodies) != 28 {
		t.Fatal("directional bodies")
	}
	for name, value := range bodies {
		shape := value.(map[string]any)
		if len(shape["vertices_milli"].([]any)) < 3 || len(shape["body_mask_hex"].(string)) != 256 {
			t.Fatal("invalid body", name)
		}
	}
	if integer(p["player"].(map[string]any), "dodge_duration_ms") != 375 {
		t.Fatal("dodge")
	}
	for _, seed := range []int64{1, 17, 20261012} {
		w, _, e := c.P4World(seed)
		if e != nil {
			t.Fatal(e)
		}
		if len(w["rooms"].([]any)) != 15 {
			t.Fatal("room count")
		}
	}
	binding := *c.V3
	binding.Protocol = 6
	if binding.validFor(c) {
		t.Fatal("old protocol admitted")
	}
	input, err := DecodeJSON(store.JSON(map[string]any{"instance_id": "dc_local_01", "boot_id": "11111111-1111-4111-8111-111111111111", "protocol_version": 7, "versions": c.V3, "config_versions": []ConfigVersion{{ID: c.Rules.Version, Hash: c.Hash}}}))
	if err != nil {
		t.Fatal(err)
	}
	paths := c.OpenAPI["paths"].(map[string]any)
	for _, value := range paths {
		post, ok := value.(map[string]any)["post"].(map[string]any)
		if !ok {
			continue
		}
		body, ok := post["requestBody"].(map[string]any)
		if !ok {
			continue
		}
		definition := body["content"].(map[string]any)["application/json"].(map[string]any)["schema"].(map[string]any)
		if props, ok := definition["properties"].(map[string]any); ok {
			for name, prop := range props {
				if _, valid := prop.(map[string]any); !valid {
					t.Fatal("non-schema property", name)
				}
			}
			if props["boot_id"] != nil && props["config_versions"] != nil {
				if e := ValidateSchema(input, definition); e != nil {
					t.Fatal(e)
				}
			}
		}
	}
}
