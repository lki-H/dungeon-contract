package gameplay_test

import (
	"encoding/json"
	"errors"
	"strings"
	"testing"
	"time"

	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/store"
)

func TestS2PauseAdmissionAndZeroEconomy(test *testing.T) {
	test.Setenv("DC_S2_TEST", "1")
	fixture := setup(test, 1)
	fixture.create(test, "")
	fixture.start(test)
	if fixture.game.Content.Rules.Version != "dc_ruins.v4.s2" || fixture.game.Content.ProtocolVersion() != 4 {
		test.Fatal("S2 fixture selected the wrong content")
	}

	binding := *fixture.game.Content.V3
	binding.Protocol = 3
	if _, err := fixture.game.Issue(fixture.ctx, gameplay.Admission{
		Versions:  &binding,
		Hash:      fixture.game.Content.Hash,
		Player:    fixture.players[0],
		RunID:     fixture.state.ID,
		Operation: uuid(),
	}); !errors.Is(err, store.Forbidden) {
		test.Fatal("S2 accepted an old protocol admission", err)
	}

	fixture.clock = fixture.clock.Add(time.Second)
	request := map[string]any{"run_id": fixture.state.ID, "reason": "menu", "expected_clock_revision": 1, "pause_id": nil}
	operation := store.ID("s2_pause")
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
	ack := gameplay.S1Ack{
		Protocol:   4,
		Assignment: fixture.manifest.AssignmentID,
		Boot:       fixture.manifest.BootID,
		Generation: fixture.manifest.Generation,
		Operation:  operation,
		Control:    pending.Control,
		Action:     "pause",
		Frame:      30,
		Game:       1000,
		Wall:       1000,
		Pause:      0,
		PauseID:    pending.PauseID,
		Revision:   2,
		Checkpoint: strings.Repeat("a", 64),
	}
	if _, err := fixture.game.ClockAck(fixture.ctx, fixture.state.ID, ack); err != nil {
		test.Fatal(err)
	}
	fixture.clock = fixture.clock.Add(2 * time.Second)
	resumeOperation := store.ID("s2_resume")
	request["reason"] = "explicit_resume"
	request["expected_clock_revision"] = 2
	request["pause_id"] = pending.PauseID
	if _, err := fixture.app.Command(fixture.ctx, fixture.players[0], resumeOperation, "v2.run.resume", store.JSON(request)); err != nil {
		test.Fatal(err)
	}
	ack.Operation = resumeOperation
	ack.Control++
	ack.Action = "resume"
	ack.Wall = 3000
	ack.Pause = 2000
	ack.Revision = 3
	if _, err := fixture.game.ClockAck(fixture.ctx, fixture.state.ID, ack); err != nil {
		test.Fatal(err)
	}

	var gameElapsed, wallElapsed, paused, rewards int64
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT game_elapsed_ms,wall_elapsed_ms,manual_pause_ms FROM pve_dc_v4_run_clocks WHERE run_id=?", fixture.state.ID).Scan(&gameElapsed, &wallElapsed, &paused); err != nil {
		test.Fatal(err)
	}
	if gameElapsed != 1000 || wallElapsed != 3000 || paused != 2000 {
		test.Fatal("S2 clock equation", gameElapsed, wallElapsed, paused)
	}
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_reward_grants WHERE run_id=?", fixture.state.ID).Scan(&rewards); err != nil {
		test.Fatal(err)
	}
	if rewards != 0 {
		test.Fatal("S2 created gameplay rewards")
	}
}
