package gameplay

import (
	"game-realtime-gm/backend/internal/pve/store"
	"path/filepath"
	"testing"
)

func TestP5FrozenProfileAndTrialBoundary(test *testing.T) {
	root := filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config")
	current, err := LoadContent(filepath.Join(root, "demo-v5-p5.json"))
	if err != nil {
		test.Fatal(err)
	}
	previous, err := LoadContent(filepath.Join(root, "demo-v5-p4.json"))
	if err != nil {
		test.Fatal(err)
	}
	service := &Service{Content: current}
	if !service.P5() || !service.P3() || !service.P4() || !service.V5() || !service.S1() || service.StagePlayerLimit() != 4 {
		test.Fatal("P5 dispatch")
	}
	if current.Hash == previous.Hash || current.WorldHash != previous.WorldHash || current.Rules.Version != "dc_ruins.v5.p5.1" || current.V3.Content != "dc_content.v5.p5.1" {
		test.Fatal("profile identity or unchanged world")
	}
	if text(current.Section("trial"), "version") != "trial.v5.1" || len(current.Rules.Tasks) != 6 {
		test.Fatal("trial or contract count")
	}
	for _, definition := range current.Rules.Tasks {
		if definition.Version != "dc_tasks.v5.p4.1" {
			test.Fatal("semantic progress identity changed")
		}
	}
	if (&Service{Content: previous}).P5() {
		test.Fatal("old P4 gained P5 privileges")
	}
}

func TestP5ActualHTTPRegistrationContract(test *testing.T) {
	content, err := LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v5-p5.json"))
	if err != nil {
		test.Fatal(err)
	}
	input, err := DecodeJSON(store.JSON(map[string]any{"instance_id": "dc_local_01", "boot_id": "11111111-1111-4111-8111-111111111111", "protocol_version": 5, "versions": content.V3, "config_versions": []ConfigVersion{{ID: content.Rules.Version, Hash: content.Hash}}}))
	if err != nil {
		test.Fatal(err)
	}
	schema := content.OpenAPI["components"].(map[string]any)["schemas"].(map[string]any)["RegisterRequest"].(map[string]any)
	if err := ValidateSchema(input, schema); err != nil {
		test.Fatal("P5 HTTP registration", err)
	}
	input.(map[string]any)["config_versions"] = []any{map[string]any{"definition_id": "dc_ruins.v5.p4.1", "hash": content.Hash}}
	if ValidateSchema(input, schema) == nil {
		test.Fatal("old profile admitted through P5 contract")
	}
}
