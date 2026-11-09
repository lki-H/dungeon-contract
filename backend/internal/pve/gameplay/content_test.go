package gameplay

import (
	"encoding/json"
	"game-realtime-gm/backend/internal/pve/run"
	"game-realtime-gm/backend/internal/pve/store"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func contentPath() string {
	return filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v1.json")
}
func TestContentAndReliableFrameSchemas(test *testing.T) {
	content, err := LoadContent(contentPath())
	if err != nil {
		test.Fatal(err)
	}
	if len(content.Rules.Tasks) != 6 || len(content.Rules.Objectives) != 3 {
		test.Fatal("adapter changed content")
	}
	if content.WorldHash == "" || text(content.World, "definition_id") != "dc_graybox.v1" {
		test.Fatal("P1 world extension not versioned or hashed")
	}
	service := Service{Content: content}
	for _, name := range []string{"frame.example.json", "terminal-frame.example.json"} {
		data, err := os.ReadFile(filepath.Join(filepath.Dir(contentPath()), "..", "contracts", name))
		if err != nil {
			test.Fatal(err)
		}
		if _, err := service.ParseFrame(data); err != nil {
			test.Fatal(name, err)
		}
	}
	for _, data := range []string{`{"field":1,"field":2}`, `{"number":1.5}`, `{"number":9007199254740992}`, `{} {}`} {
		if _, err := DecodeJSON([]byte(data)); err == nil {
			test.Fatal("invalid JSON accepted", data)
		}
	}
	var raw map[string]any
	data, _ := os.ReadFile(contentPath())
	decoder := json.NewDecoder(strings.NewReader(string(data)))
	decoder.UseNumber()
	if err := decoder.Decode(&raw); err != nil {
		test.Fatal(err)
	}
	raw["authority"].(map[string]any)["heartbeat_ms"] = json.Number("7000")
	invalid := &Content{Document: raw}
	if err := invalid.validate(); err == nil {
		test.Fatal("invalid heartbeat/lease accepted")
	}
	candidates := content.Candidates(123, "choice_1", "ranger", nil)
	if len(candidates) != 3 {
		test.Fatal("candidate count")
	}
	if string(Canonical(mustJSON(store.JSON(candidates)))) != string(Canonical(mustJSON(store.JSON(content.Candidates(123, "choice_1", "ranger", nil))))) {
		test.Fatal("candidate randomness not repeatable")
	}
}
func mustJSON(data []byte) any {
	value, err := DecodeJSON(data)
	if err != nil {
		panic(err)
	}
	return value
}

func TestWorldGeometryRejectsUnplayableConfigurations(test *testing.T) {
	for _, scenario := range []struct {
		name string
		edit func(map[string]any)
	}{
		{"reversed doors", func(world map[string]any) {
			world["encounter"].(map[string]any)["door_rows"] = []any{json.Number("6"), json.Number("5")}
		}},
		{"boss outside room", func(world map[string]any) {
			world["encounter"].(map[string]any)["boss_spawn"] = []any{json.Number("8"), json.Number("14")}
		}},
		{"supply inside wall", func(world map[string]any) {
			world["encounter"].(map[string]any)["supply_position"] = []any{json.Number("1"), json.Number("3")}
		}},
		{"unsafe projectile sweep", func(world map[string]any) {
			world["physics"].(map[string]any)["sweep_step_milli"] = json.Number("200")
			world["physics"].(map[string]any)["projectile_radius_milli"] = json.Number("10")
		}},
	} {
		test.Run(scenario.name, func(subtest *testing.T) {
			content, err := LoadContent(contentPath())
			if err != nil {
				subtest.Fatal(err)
			}
			scenario.edit(content.World)
			if content.validateWorld() == nil {
				subtest.Fatal("unplayable world configuration accepted")
			}
		})
	}
}

func TestRuntimeContractValidatesDirectResponses(test *testing.T) {
	content, err := LoadContent(contentPath())
	if err != nil {
		test.Fatal(err)
	}
	paths := content.OpenAPI["paths"].(map[string]any)
	for _, scenario := range []struct {
		path, method string
		value        any
	}{
		{"/internal/dc/v1/instances/register", "post", map[string]any{"registered": true, "instance_id": "dc_local_01", "source_generation": 1}},
		{"/internal/dc/v1/instances/{instance_id}/claim", "post", map[string]any{"assignment": nil}},
		{"/internal/dc/v1/runs/{run_id}/start", "post", map[string]any{"start_operation": "start:run_test", "run_id": "run_test"}},
		{"/internal/dc/v1/runs/{run_id}/start", "post", map[string]any{"ready": true, "run_id": "run_test"}},
		{"/internal/dc/v1/runs/{run_id}/frames", "post", &Receipt{Status: "applied", Applied: 1, RunStatus: "running", Reinforcements: map[string]run.Spawn{}, Requirements: map[string]map[string]int64{}}},
	} {
		operation := paths[scenario.path].(map[string]any)[scenario.method].(map[string]any)
		schema := operation["responses"].(map[string]any)["200"].(map[string]any)["content"].(map[string]any)["application/json"].(map[string]any)["schema"].(map[string]any)
		if err := ValidateSchema(mustJSON(store.JSON(scenario.value)), schema); err != nil {
			test.Fatal(scenario.path, err)
		}
	}
}
