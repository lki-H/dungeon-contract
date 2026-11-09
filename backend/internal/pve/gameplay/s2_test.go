package gameplay_test

import (
	"game-realtime-gm/backend/internal/pve/gameplay"
	"path/filepath"
	"testing"
)

func TestS2ContentUsesNewWeaponParameters(test *testing.T) {
	content, err := gameplay.LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v4-s2.json"))
	if err != nil {
		test.Fatal(err)
	}
	if content.Rules.Version != "dc_ruins.v4.s2" || content.ProtocolVersion() != 4 || content.Rules.SuccessReward != 0 || len(content.Rules.Tasks) != 0 {
		test.Fatal("S2 isolation")
	}
	parameters := content.World["parameters"].(map[string]any)
	if parameters["specification_version"] != "2.1" {
		test.Fatal("S2 retained old weapon parameters")
	}
}
