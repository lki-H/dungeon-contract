package gameplay_test

import (
	"errors"
	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/store"
	"strings"
	"testing"
)

func TestP2MultiplayerStorageLifecycle(test *testing.T) {
	for _, count := range []int{2, 4} {
		test.Run(string(rune('0'+count)), func(test *testing.T) {
			test.Setenv("DC_P2_MP_TEST", "1")
			fixture := setup(test, count)
			fixture.create(test, "")
			fixture.start(test)
			if len(fixture.manifest.Members) != count {
				test.Fatal("Frozen roster mismatch")
			}
			_, err := fixture.app.Command(fixture.ctx, fixture.players[0], store.ID("pause"), "v2.run.pause", store.JSON(map[string]any{"run_id": fixture.state.ID, "reason": "menu", "expected_clock_revision": 1, "pause_id": nil}))
			if !errors.Is(err, store.Forbidden) {
				test.Fatal("Multiplayer pause permitted", err)
			}
			binding := *fixture.game.Content.V3
			binding.Protocol = 3
			_, err = fixture.game.Issue(fixture.ctx, gameplay.Admission{Versions: &binding, Hash: fixture.game.Content.Hash, Player: fixture.players[0], RunID: fixture.state.ID, Operation: uuid()})
			if !errors.Is(err, store.Forbidden) {
				test.Fatal("Old protocol admitted", err)
			}
			input := gameplay.P1Result{Operation: uuid(), Assignment: fixture.manifest.AssignmentID, Generation: fixture.manifest.Generation, Outcome: "technical_failure", Completed: []bool{true, false, false, false, false, false}, SummaryHash: strings.Repeat("a", 64)}
			_, err = fixture.game.FinishP1(fixture.ctx, fixture.state.ID, input)
			if err != nil {
				test.Fatal("Stage reliable finish", err)
			}
			var rewards int
			if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_reward_grants WHERE run_id=?", fixture.state.ID).Scan(&rewards); err != nil || rewards != 0 {
				test.Fatal("Stage created reward", err)
			}
		})
	}
}
