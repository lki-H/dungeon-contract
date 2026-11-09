package gameplay

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"slices"
	"strings"
	"testing"

	"game-realtime-gm/backend/internal/pve/store"
)

func p4Content(test *testing.T) *Content {
	test.Helper()
	content, err := LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v4-p4.json"))
	if err != nil {
		test.Fatal(err)
	}
	return content
}
func TestP4ProfileSeededBranchesAndVariants(test *testing.T) {
	content := p4Content(test)
	if content.Rules.Version != "dc_ruins.v4.p4.1" || len(content.Rules.Tasks) != 6 || len(objects(content.Document, "upgrades")) != 10 || content.Rules.Tasks[0].Version != "dc_ruins.v4.p3.1" {
		test.Fatal("profile or contract semantic boundary")
	}
	before := store.Hash(Canonical(content.World))
	combinations := map[string]bool{}
	for seed := int64(0); seed < 256; seed++ {
		world, hash, err := content.P4World(seed)
		if err != nil {
			test.Fatal(err)
		}
		again, againHash, err := content.P4World(seed)
		if err != nil || hash != againHash || string(Canonical(world)) != string(Canonical(again)) {
			test.Fatal("unstable seed")
		}
		rooms := world["rooms"].([]any)
		combinations[fmt.Sprint(rooms[6].(map[string]any)["exists"], rooms[7].(map[string]any)["exists"])] = true
		for _, index := range []int{1, 2, 3, 4} {
			room := rooms[index].(map[string]any)
			expected := 2
			if index == 4 {
				expected = 1
			}
			if len(room["obstacles_milli"].([]any)) != expected {
				test.Fatal("A/B obstacles combined")
			}
		}
		for _, value := range world["doors"].([]any) {
			door := value.(map[string]any)
			if rooms[integer(door, "from")].(map[string]any)["exists"] != true || rooms[integer(door, "to")].(map[string]any)["exists"] != true {
				test.Fatal("absent branch door")
			}
		}
	}
	if len(combinations) != 4 || store.Hash(Canonical(content.World)) != before {
		test.Fatal("random independence or template mutation")
	}
}
func TestP4ActualHTTPRegistrationContract(test *testing.T) {
	content := p4Content(test)
	request := InstanceRequest{InstanceID: "dc_local_01", BootID: "11111111-1111-4111-8111-111111111111", ProtocolVersion: 4, Versions: content.V3, ConfigVersions: []ConfigVersion{{ID: content.Rules.Version, Hash: content.Hash}}}
	value, err := DecodeJSON(store.JSON(map[string]any{"instance_id": request.InstanceID, "boot_id": request.BootID, "protocol_version": request.ProtocolVersion, "versions": request.Versions, "config_versions": request.ConfigVersions}))
	if err != nil {
		test.Fatal(err)
	}
	schema := content.OpenAPI["components"].(map[string]any)["schemas"].(map[string]any)["RegisterRequest"].(map[string]any)
	if err = ValidateSchema(value, schema); err != nil {
		test.Fatal("HTTP register schema", err)
	}
	request.Versions = &VersionBinding{Protocol: 4, Operation: "dc_ruins.v4", Content: "dc_content.v4.draft3", World: "dc_rooms.v4.draft2", ParametersHash: content.V3.ParametersHash}
	value, _ = DecodeJSON(store.JSON(map[string]any{"instance_id": request.InstanceID, "boot_id": request.BootID, "protocol_version": request.ProtocolVersion, "versions": request.Versions, "config_versions": request.ConfigVersions}))
	if ValidateSchema(value, schema) == nil {
		test.Fatal("old content admitted")
	}
}
func TestP4EveryGrowthHistoryRetainsThreeLegalOptions(test *testing.T) {
	content := p4Content(test)
	for _, kit := range []string{"ranger", "runesmith"} {
		var walk func([]string)
		walk = func(selected []string) {
			if len(selected) == 3 {
				return
			}
			options := content.Candidates(20261012, fmt.Sprintf("growth_%d", len(selected)+1), kit, selected)
			if len(options) != 3 {
				test.Fatal("candidate shortage", selected)
			}
			for _, option := range options {
				if slices.Contains(selected, option) {
					test.Fatal("duplicate")
				}
				walk(append(slices.Clone(selected), option))
			}
		}
		walk(nil)
	}
}
func TestP4ReliableChoiceValidationAndDefault(test *testing.T) {
	content := p4Content(test)
	service := &Service{Content: content}
	newAllocation := func() *Allocation {
		return &Allocation{Manifest: Manifest{Seed: 20261012}, Stats: map[string]Stats{"1": {PlayerID: "1", LoadoutID: "ranger", Choices: []string{}}}}
	}
	options := content.Candidates(20261012, "growth_1", "ranger", nil)
	action := "growth_action_valid"
	fact := Fact{Actor: ptr("1"), Action: &action, Kind: "growth_choice", Target: options[0], Proof: store.JSON(map[string]any{"node_id": "growth_1", "candidate_revision": 1, "candidate_hash": store.Hash([]byte(strings.Join(options, "|"))), "opened_game_ms": 1000, "deadline_game_ms": 61000, "automatic": false})}
	allocation := newAllocation()
	if err := service.applyP4Choice(allocation, fact, Frame{Elapsed: 1001}); err != nil {
		test.Fatal(err)
	}
	if len(allocation.Stats["1"].Choices) != 1 {
		test.Fatal("not durable")
	}
	if service.applyP4Choice(allocation, fact, Frame{Elapsed: 1002}) == nil {
		test.Fatal("duplicate accepted")
	}
	if service.applyP4Choice(newAllocation(), fact, Frame{Elapsed: 61000}) == nil {
		test.Fatal("late manual")
	}
	for _, change := range []map[string]any{{"candidate_hash": strings.Repeat("0", 64)}, {"node_id": "growth_2"}, {"candidate_revision": 0}, {"opened_game_ms": 2000}, {"deadline_game_ms": 62000}, {"automatic": true}} {
		corrupt := fact
		value, _ := DecodeJSON(fact.Proof)
		proof := value.(map[string]any)
		for key, value := range change {
			proof[key] = value
		}
		corrupt.Proof = store.JSON(proof)
		if service.applyP4Choice(newAllocation(), corrupt, Frame{Elapsed: 1001}) == nil {
			test.Fatal("invalid accepted", change)
		}
	}
	for _, mode := range []string{"missing", "extra"} {
		value, _ := DecodeJSON(fact.Proof)
		proof := value.(map[string]any)
		if mode == "missing" {
			delete(proof, "automatic")
		} else {
			proof["origin_life_generation"] = 1
		}
		corrupt := fact
		corrupt.Proof = store.JSON(proof)
		if service.applyP4Choice(newAllocation(), corrupt, Frame{Elapsed: 1001}) == nil {
			test.Fatal("non-exact proof", mode)
		}
	}
	value, _ := DecodeJSON(fact.Proof)
	proof := value.(map[string]any)
	proof["automatic"] = true
	fact.Proof = store.JSON(proof)
	if err := service.applyP4Choice(newAllocation(), fact, Frame{Elapsed: 61000}); err != nil {
		test.Fatal("default", err)
	}
	fact.Target = options[1]
	if service.applyP4Choice(newAllocation(), fact, Frame{Elapsed: 61000}) == nil {
		test.Fatal("nonfirst automatic")
	}
}
func ptr(value string) *string { return &value }
func TestP4DocumentedChoiceActionAndRecovery(test *testing.T) {
	base := filepath.Join("..", "..", "..", "..", "docs", "game-demo", "contracts")
	schemaData, err := os.ReadFile(filepath.Join(base, "action-v4-p4.schema.json"))
	if err != nil {
		test.Fatal(err)
	}
	exampleData, err := os.ReadFile(filepath.Join(base, "p4-example.json"))
	if err != nil {
		test.Fatal(err)
	}
	schemaValue, err := DecodeJSON(schemaData)
	if err != nil {
		test.Fatal(err)
	}
	exampleValue, err := DecodeJSON(exampleData)
	if err != nil {
		test.Fatal(err)
	}
	example := exampleValue.(map[string]any)
	schema := schemaValue.(map[string]any)
	if err = ValidateSchema(example["action"], schema); err != nil {
		test.Fatal(err)
	}
	action := example["action"].(map[string]any)
	action["choice_node"] = json.Number("3")
	if ValidateSchema(action, schema) == nil {
		test.Fatal("fourth node")
	}
	recovery := example["recovery"].(map[string]any)
	if integer(recovery, "deadline_game_ms")-integer(recovery, "current_game_ms") != integer(recovery, "remaining_ms") {
		test.Fatal("clock fixture")
	}
}
