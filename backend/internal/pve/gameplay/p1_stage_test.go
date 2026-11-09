package gameplay_test

import (
	"errors"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/store"
)

func TestP1V3ContentIsolation(test *testing.T) {
	content, err := gameplay.LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v3-p1.json"))
	if err != nil {
		test.Fatal(err)
	}
	if content.Rules.Version != "dc_ruins.v3.p1" || content.ProtocolVersion() != 3 || len(content.Rules.Tasks) != 0 || content.Rules.SuccessReward != 0 || content.Rules.FailureReward != 0 {
		test.Fatal("P1 stage published gameplay economy")
	}
	if content.World["parameters"] == nil {
		test.Fatal("P1 geometry missing frozen parameters")
	}
}
func TestP1V3ResultReliableAndZeroRewards(test *testing.T) {
	test.Setenv("DC_V3_P1_TEST", "1")
	fixture := setup(test, 1)
	fixture.create(test, "")
	fixture.start(test)
	fixture.clock = fixture.clock.Add(time.Second)
	input := gameplay.P1Result{Operation: uuid(), Assignment: fixture.manifest.AssignmentID, Generation: fixture.manifest.Generation, Control: 0, Outcome: "success", Frame: 30, Elapsed: 1000, Completed: []bool{true, true, true, true, true, true}, SummaryHash: strings.Repeat("a", 64)}
	wrong := input
	wrong.Completed = []bool{true, false, false, false, false, false}
	if _, err := fixture.game.FinishP1(fixture.ctx, fixture.state.ID, wrong); !errors.Is(err, store.Conflict) {
		test.Fatal("Incomplete P1 success accepted", err)
	}
	wrong = input
	wrong.Generation++
	if _, err := fixture.game.FinishP1(fixture.ctx, fixture.state.ID, wrong); !errors.Is(err, store.Forbidden) {
		test.Fatal("Old P1 source accepted", err)
	}
	if _, err := fixture.game.FinishP1(fixture.ctx, fixture.state.ID, input); err != nil {
		test.Fatal(err)
	}
	if _, err := fixture.game.FinishP1(fixture.ctx, fixture.state.ID, input); err != nil {
		test.Fatal("P1 result was not idempotent", err)
	}
	if err := fixture.app.Settlement.Settle(fixture.ctx, fixture.state.ID); err != nil {
		test.Fatal(err)
	}
	if _, err := fixture.db.ExecContext(fixture.ctx, "UPDATE pve_dc_allocations SET manifest=JSON_SET(manifest,'$.config_hash',?) WHERE run_id=?", strings.Repeat("0", 64), fixture.state.ID); err != nil {
		test.Fatal(err)
	}
	if err := fixture.game.Tick(fixture.ctx); err != nil {
		test.Fatal("Closed historical hash blocked current maintenance", err)
	}
	var rewards int
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_reward_grants WHERE run_id=?", fixture.state.ID).Scan(&rewards); err != nil || rewards != 0 {
		test.Fatal("P1 stage manufactured gameplay rewards", err)
	}
}
