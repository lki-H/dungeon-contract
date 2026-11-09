package gameplay

import (
	"encoding/json"
	"path/filepath"
	"testing"

	"game-realtime-gm/backend/internal/pve/store"
)

func TestV5FrozenContentAndBusinessStages(test *testing.T) {
	content, err := LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v5-p4.json"))
	if err != nil {
		test.Fatal(err)
	}
	service := &Service{Content: content}
	if !service.V5() || !service.P4() || !service.P3() || !service.S1() || service.StagePlayerLimit() != 4 || content.ProtocolVersion() != 5 {
		test.Fatal("v5 stage dispatch")
	}
	if len(content.Rules.Tasks) != 6 || integer(content.Section("authority"), "max_projectiles") != 1024 {
		test.Fatal("v5 budgets or contracts")
	}
	for _, task := range content.Rules.Tasks {
		if task.Version != "dc_tasks.v5.p4.1" {
			test.Fatal("old task identity")
		}
	}
	for _, scheme := range []string{"ranger", "runesmith"} {
		for _, first := range content.Candidates(1, "growth_1", scheme, nil) {
			for _, second := range content.Candidates(1, "growth_2", scheme, []string{first}) {
				if len(content.Candidates(1, "growth_3", scheme, []string{first, second})) != 3 {
					test.Fatal("growth candidates")
				}
			}
		}
	}
}

func TestV5SummonOriginCannotAdvanceContract(test *testing.T) {
	actor, action := "101", "root_1"
	frame := Frame{Schema: 5, Source: "unity", Elapsed: 100, WallElapsed: 100, Facts: []Fact{{EventGameMS: 100, Kind: "damage", Actor: &actor, Action: &action, Target: "guard_000001", LifeGeneration: 1, Amount: 12, SpawnOrigin: "main_wave", Proof: json.RawMessage("null")}}}
	if err := validateV5Frame(frame); err != nil {
		test.Fatal(err)
	}
	for _, origin := range []string{"boss_summon", "practice", ""} {
		frame.Facts[0].SpawnOrigin = origin
		if err := validateV5Frame(frame); err != store.Forbidden {
			test.Fatalf("accepted origin %q: %v", origin, err)
		}
	}
	frame.Facts[0].SpawnOrigin = "main_wave"
	frame.Facts[0].Target = "summon_elite_000001"
	if err := validateV5Frame(frame); err != store.Forbidden {
		test.Fatal("summon identity washed")
	}
	frame.Schema = 4
	if validateV5Frame(frame) == nil {
		test.Fatal("legacy frame accepted")
	}
}

func TestV5ActualHTTPRegistrationContract(test *testing.T) {
	content, err := LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v5-p4.json"))
	if err != nil {
		test.Fatal(err)
	}
	value, err := DecodeJSON(store.JSON(map[string]any{"instance_id": "dc_local_01", "boot_id": "11111111-1111-4111-8111-111111111111", "protocol_version": 5, "versions": content.V3, "config_versions": []ConfigVersion{{ID: content.Rules.Version, Hash: content.Hash}}}))
	if err != nil {
		test.Fatal(err)
	}
	schema := content.OpenAPI["components"].(map[string]any)["schemas"].(map[string]any)["RegisterRequest"].(map[string]any)
	if err := ValidateSchema(value, schema); err != nil {
		test.Fatal("v5 HTTP register", err)
	}
	value.(map[string]any)["protocol_version"] = 4
	if ValidateSchema(value, schema) == nil {
		test.Fatal("legacy protocol admitted")
	}
}
