package gameplay_test

import (
	"encoding/json"
	"game-realtime-gm/backend/internal/pve/gameplay"
	"path/filepath"
	"testing"
)

func TestP21ContentIsolation(test *testing.T) {
	content, err := gameplay.LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v4-p2-1.json"))
	if err != nil {
		test.Fatal(err)
	}
	if content.Rules.Version != "dc_ruins.v4.p2.1" || content.ProtocolVersion() != 4 || content.Rules.SuccessReward != 0 || len(content.Rules.Tasks) != 0 {
		test.Fatal("P2.1 isolation")
	}
	if len(content.World["rooms"].([]any)) != 15 || content.World["parameters"].(map[string]any)["specification_version"] != "2.1-p2.1" {
		test.Fatal("P2.1 world binding")
	}
}

func TestP21RejectsInvalidGeometry(test *testing.T) {
	content, err := gameplay.LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v4-p2-1.json"))
	if err != nil {
		test.Fatal(err)
	}
	mutations := map[string]func(map[string]any){
		"foreign room kind": func(world map[string]any) { world["rooms"].([]any)[0].(map[string]any)["kind"] = "corridor" },
		"corridor encounter object": func(world map[string]any) {
			world["rooms"].([]any)[8].(map[string]any)["trap_points"] = []any{[]any{json.Number("2"), json.Number("2")}}
		},
		"corridor too narrow": func(world map[string]any) { world["rooms"].([]any)[8].(map[string]any)["height"] = json.Number("4") },
		"door shortcut":       func(world map[string]any) { world["doors"].([]any)[0].(map[string]any)["to"] = json.Number("5") },
		"invalid door side":   func(world map[string]any) { world["doors"].([]any)[0].(map[string]any)["side"] = "north" },
		"inconsistent camera": func(world map[string]any) {
			world["camera"].(map[string]any)["view_width_milli_tiles"] = json.Number("32000")
		},
		"oversized room": func(world map[string]any) { world["rooms"].([]any)[2].(map[string]any)["width"] = json.Number("35") },
	}
	for name, mutate := range mutations {
		test.Run(name, func(test *testing.T) {
			data, err := json.Marshal(content.World)
			if err != nil {
				test.Fatal(err)
			}
			value, err := gameplay.DecodeJSON(data)
			if err != nil {
				test.Fatal(err)
			}
			world := value.(map[string]any)
			mutate(world)
			if gameplay.ValidateP2Geometry(world) == nil {
				test.Fatal("invalid geometry accepted")
			}
		})
	}
}
