package gameplay_test

import (
	"encoding/json"
	"errors"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/store"
)

func TestS1ContentVersionAndIsolation(test *testing.T) {
	content, err := gameplay.LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v4-s1.json"))
	if err != nil {
		test.Fatal(err)
	}
	if content.ProtocolVersion() != 4 || content.Rules.Version != "dc_ruins.v4.s1" || len(content.Rules.Tasks) != 0 || content.Rules.SuccessReward != 0 || content.Rules.Reinforcements != 0 {
		test.Fatal("S1 version/economy boundary")
	}
}

func TestS1PauseResumeSourceAndClock(test *testing.T) {
	test.Setenv("DC_S1_TEST", "1")
	fixture := setup(test, 1)
	fixture.create(test, "")
	fixture.start(test)
	badBinding := *fixture.game.Content.V3
	badBinding.Protocol = 3
	if _, err := fixture.game.Issue(fixture.ctx, gameplay.Admission{Versions: &badBinding, Hash: fixture.game.Content.Hash, Player: fixture.players[0], RunID: fixture.state.ID, Operation: uuid()}); !errors.Is(err, store.Forbidden) {
		test.Fatal("old protocol admission", err)
	}
	fixture.clock = fixture.clock.Add(time.Second)
	request := map[string]any{"run_id": fixture.state.ID, "reason": "menu", "expected_clock_revision": 1, "pause_id": nil}
	operation := store.ID("pause")
	raw, err := fixture.app.Command(fixture.ctx, fixture.players[0], operation, "v2.run.pause", store.JSON(request))
	if err != nil {
		test.Fatal(err)
	}
	var pending struct {
		PauseID string `json:"pause_id"`
		Control int64  `json:"control_version"`
	}
	if err := json.Unmarshal(raw, &pending); err != nil {
		test.Fatal(err)
	}
	ack := gameplay.S1Ack{Protocol: 4, Assignment: fixture.manifest.AssignmentID, Boot: fixture.manifest.BootID, Generation: fixture.manifest.Generation, Operation: operation, Control: pending.Control, Action: "pause", Frame: 30, Game: 1000, Wall: 1000, Pause: 0, PauseID: pending.PauseID, Revision: 2, Checkpoint: strings.Repeat("a", 64)}
	bad := ack
	bad.Generation++
	if _, err := fixture.game.ClockAck(fixture.ctx, fixture.state.ID, bad); !errors.Is(err, store.Forbidden) {
		test.Fatal("old source", err)
	}
	if _, err := fixture.game.ClockAck(fixture.ctx, fixture.state.ID, ack); err != nil {
		test.Fatal(err)
	}
	if _, err := fixture.game.ClockAck(fixture.ctx, fixture.state.ID, ack); err != nil {
		test.Fatal("duplicate", err)
	}
	bad = ack
	bad.Checkpoint = strings.Repeat("b", 64)
	if _, err := fixture.game.ClockAck(fixture.ctx, fixture.state.ID, bad); !errors.Is(err, store.Conflict) {
		test.Fatal("conflicting ACK", err)
	}
	fixture.clock = fixture.clock.Add(2 * time.Second)
	resumeOp := store.ID("resume")
	request = map[string]any{"run_id": fixture.state.ID, "reason": "explicit_resume", "expected_clock_revision": 2, "pause_id": pending.PauseID}
	if _, err := fixture.app.Command(fixture.ctx, fixture.players[0], resumeOp, "v2.run.resume", store.JSON(request)); err != nil {
		test.Fatal(err)
	}
	ack.Operation = resumeOp
	ack.Control++
	ack.Action = "resume"
	ack.Wall = 3000
	ack.Pause = 2000
	ack.Revision = 3
	if _, err := fixture.game.ClockAck(fixture.ctx, fixture.state.ID, ack); err != nil {
		test.Fatal(err)
	}
	var paused, wall, game, intervals int64
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT game_elapsed_ms,wall_elapsed_ms,manual_pause_ms FROM pve_dc_v4_run_clocks WHERE run_id=?", fixture.state.ID).Scan(&game, &wall, &paused); err != nil {
		test.Fatal(err)
	}
	if game != 1000 || wall != 3000 || paused != 2000 {
		test.Fatal("clock equation", game, wall, paused)
	}
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_dc_v4_pause_intervals WHERE run_id=? AND status='closed'", fixture.state.ID).Scan(&intervals); err != nil || intervals != 1 {
		test.Fatal("pause durable interval", err)
	}
	var budget int
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_reward_grants WHERE run_id=?", fixture.state.ID).Scan(&budget); err != nil || budget != 0 {
		test.Fatal("clock created gameplay rewards", err)
	}
}
