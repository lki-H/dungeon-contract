package gameplay

import (
	"game-realtime-gm/backend/internal/pve/store"
	"os"
	"path/filepath"
	"testing"
)

func TestV8FrozenContentAndIdentity(t *testing.T) {
	c, err := LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v8-p5.json"))
	if err != nil {
		t.Fatal(err)
	}
	if c.ProtocolVersion() != 8 || c.Rules.Version != "dc_ruins.v8.p5.1" || len(c.Rules.Tasks) != 6 {
		t.Fatal("version/task boundary")
	}
	versions := c.Document["versions"].(map[string]any)
	unityBinding := &VersionBinding{Protocol: int(integer(c.Document, "protocol_version")), Operation: text(versions, "operation"), Content: text(versions, "content"), World: text(versions, "world"), ParametersHash: text(c.Document, "parameters_sha256")}
	if !unityBinding.validFor(c) {
		t.Fatalf("Unity binding %#v differs from control %#v", unityBinding, c.V3)
	}
	if c.FrameSchema["$id"] != "urn:dc:frame:8:p5.1" || c.OpenAPI["info"].(map[string]any)["version"] != "8.p5.1" {
		t.Fatal("v8 contract identity still names an inherited release")
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
		if path := os.Getenv("DC_V8_MANIFEST_MODEL_FILE"); path != "" && seed == 17 {
			data := store.JSON(map[string]any{"profile": "dc_ruins.v8.p5.1", "config": c.Document, "p1_world": w, "p1_world_hash": store.Hash(Canonical(w)), "parameters_hash": c.V3.ParametersHash})
			if err := os.WriteFile(path, data, 0600); err != nil {
				t.Fatal(err)
			}
		}
	}
	binding := *c.V3
	binding.Protocol = 6
	if binding.validFor(c) {
		t.Fatal("old protocol admitted")
	}
	input, err := DecodeJSON(store.JSON(map[string]any{"instance_id": "dc_local_01", "boot_id": "11111111-1111-4111-8111-111111111111", "protocol_version": 8, "versions": c.V3, "config_versions": []ConfigVersion{{ID: c.Rules.Version, Hash: c.Hash}}}))
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

func TestV8IntegerLifecycleExamples(t *testing.T) {
	base := filepath.Join("..", "..", "..", "..", "docs", "game-demo", "contracts", "runtime-v8")
	decode := func(path string) any {
		t.Helper()
		data, err := os.ReadFile(path)
		if err != nil {
			t.Fatal(err)
		}
		value, err := DecodeJSON(data)
		if err != nil {
			t.Fatal(err)
		}
		return value
	}
	schema := decode(filepath.Join(base, "combat-state.schema.json")).(map[string]any)
	// Unity world-space floats use the offline state-schema audit; business JSON stays integer-only.
	for _, name := range []string{"ready", "first-tower", "cooldown", "death-hammer", "restored"} {
		path := filepath.Join(base, name+".example.json")
		if err := ValidateSchema(decode(path), schema); err != nil {
			t.Fatalf("%s: %v", name, err)
		}
	}
	bad := decode(filepath.Join(base, "first-tower.example.json")).(map[string]any)
	value, _ := DecodeJSON([]byte(`3`))
	bad["turret_charges"] = value
	if ValidateSchema(bad, schema) == nil {
		t.Fatal("accepted excess tower charges")
	}
}
