package gameplay_test

import (
	"game-realtime-gm/backend/internal/pve/gameplay"
	"path/filepath"
	"testing"
)

func TestP2MultiplayerStageIsolation(test *testing.T) {
	content, err := gameplay.LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v4-p2-mp.json"))
	if err != nil {
		test.Fatal(err)
	}
	service := &gameplay.Service{Content: content}
	if service.StagePlayerLimit() != 4 || !service.S1() || content.Rules.Version != "dc_ruins.v4.p2.multiplayer" || content.Rules.SuccessReward != 0 || len(content.Rules.Tasks) != 0 {
		test.Fatal("Multiplayer stage isolation")
	}
	single, err := gameplay.LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v4-p2-1.json"))
	if err != nil {
		test.Fatal(err)
	}
	if (&gameplay.Service{Content: single}).StagePlayerLimit() != 1 || single.Hash == content.Hash {
		test.Fatal("Single profile became multiplayer")
	}
}
