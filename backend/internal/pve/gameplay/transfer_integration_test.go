package gameplay_test

import (
	"encoding/json"
	"errors"
	"path/filepath"
	"testing"

	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/store"
)

func TestQualityProgressTransferIsIdempotentAndKeepsCompletion(test *testing.T) {
	fixture := setup(test, 1)
	content, err := gameplay.LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v2.json"))
	if err != nil {
		test.Fatal(err)
	}
	player := fixture.players[0]
	for _, entry := range []struct {
		key       string
		completed bool
		counts    map[string]int64
	}{{"dc_hunter", false, map[string]int64{"kills": 5}}, {"dc_precision", true, map[string]int64{"hits": 6}}} {
		if _, err := fixture.db.ExecContext(fixture.ctx, "INSERT INTO pve_player_tasks(player_id,task_key,version,progress,completed,unlocked) VALUES(?,?,'dc_ruins.v1',?,?,1)", player, entry.key, store.JSON(entry.counts), entry.completed); err != nil {
			test.Fatal(err)
		}
	}
	fixture.game.Content = content
	for attempt := 0; attempt < 2; attempt++ {
		if err := fixture.game.TransferProgress(fixture.ctx); err != nil {
			test.Fatal(err)
		}
	}
	var data []byte
	var completed bool
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT progress,completed FROM pve_player_tasks WHERE player_id=? AND task_key='dc_hunter' AND version='dc_ruins.v2'", player).Scan(&data, &completed); err != nil {
		test.Fatal(err)
	}
	counts := map[string]int64{}
	if json.Unmarshal(data, &counts) != nil || counts["kills"] != 5 || completed {
		test.Fatal("ordinary progress changed")
	}
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT completed FROM pve_player_tasks WHERE player_id=? AND task_key='dc_precision' AND version='dc_ruins.v2'", player).Scan(&completed); err != nil || !completed {
		test.Fatal("completion lost", err)
	}
	var transfers, rewards int
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_dc_task_transfers WHERE player_id=?", player).Scan(&transfers); err != nil {
		test.Fatal(err)
	}
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_reward_grants WHERE player_id=?", player).Scan(&rewards); err != nil {
		test.Fatal(err)
	}
	if transfers != 2 || rewards != 0 {
		test.Fatal("transfer duplicated or granted assets", transfers, rewards)
	}
}

func TestQualityRestartDefersTransferUntilReliableSettlement(test *testing.T) {
	fixture := setup(test, 1)
	fixture.create(test, "")
	fixture.start(test)
	player := fixture.players[0]
	if _, err := fixture.db.ExecContext(fixture.ctx, "INSERT INTO pve_player_tasks(player_id,task_key,version,progress,completed,unlocked) VALUES(?,'dc_hunter','dc_ruins.v1',?,0,1)", player, store.JSON(map[string]int64{"kills": 5})); err != nil {
		test.Fatal(err)
	}
	content, err := gameplay.LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v2.json"))
	if err != nil {
		test.Fatal(err)
	}
	fixture.game.Content = content
	if err := fixture.game.TransferProgress(fixture.ctx); !errors.Is(err, gameplay.ErrProgressTransferBusy) {
		test.Fatalf("transfer must wait for active runs: %v", err)
	}
	if err := fixture.app.Recover(fixture.ctx); err != nil {
		test.Fatalf("pending transfer blocked service recovery: %v", err)
	}
	state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.Status != "ending" || state.EndReason != "server_restart" {
		test.Fatalf("restart did not preserve reliable termination: state=%+v err=%v", state, err)
	}
	if err := fixture.app.Settlement.Settle(fixture.ctx, fixture.state.ID); err != nil {
		test.Fatal(err)
	}
	if err := fixture.game.Tick(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	var data []byte
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT progress FROM pve_player_tasks WHERE player_id=? AND task_key='dc_hunter' AND version='dc_ruins.v2'", player).Scan(&data); err != nil {
		test.Fatal(err)
	}
	var counts map[string]int64
	if err := json.Unmarshal(data, &counts); err != nil || counts["kills"] != 5 {
		test.Fatalf("deferred transfer lost confirmed progress: %v", err)
	}
}
