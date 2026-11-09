package gameplay_test

import (
	"errors"
	"fmt"
	"strings"
	"testing"

	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/store"
)

func TestP4StorageIdleHeartbeatAllocationRaceRequiresReclaim(test *testing.T) {
	test.Setenv("DC_P4_TEST", "1")
	fixture := setup(test, 1)
	fixture.create(test, "")
	request := gameplay.InstanceRequest{InstanceID: "dc_local_01", BootID: fixture.manifest.BootID, Versions: fixture.game.Content.V3, Status: "idle"}
	if err := fixture.game.Heartbeat(fixture.ctx, request); !errors.Is(err, store.Forbidden) {
		test.Fatal("stale idle heartbeat unexpectedly changes binding", err)
	}
	manifest, err := fixture.game.Claim(fixture.ctx, request)
	if err != nil || manifest == nil || manifest.AssignmentID != fixture.manifest.AssignmentID {
		test.Fatal("reclaim original assigned Run", err)
	}
	request.AssignmentID = manifest.AssignmentID
	request.Generation = manifest.Generation
	request.Status = "loading"
	if err = fixture.game.Heartbeat(fixture.ctx, request); err != nil {
		test.Fatal("recovered binding", err)
	}
}

func TestP4StorageGrowthPrefixRecoveryAndSettlement(test *testing.T) {
	test.Setenv("DC_P4_TEST", "1")
	fixture := setup(test, 1)
	fixture.create(test, "")
	fixture.start(test)
	player := fixture.players[0]
	selected := []string{}
	for index := 0; index < 3; index++ {
		node := fmt.Sprintf("growth_%d", index+1)
		options := fixture.game.Content.Candidates(fixture.manifest.Seed, node, "ranger", selected)
		fact := p3Fact(player, "growth_choice", options[0], int64(index+1)*1000)
		action := store.ID("growth")
		fact.Action = &action
		fact.Proof = store.JSON(map[string]any{"node_id": node, "candidate_revision": index + 1, "candidate_hash": store.Hash([]byte(strings.Join(options, "|"))), "opened_game_ms": int64(index) * 1000, "deadline_game_ms": int64(index)*1000 + 60000, "automatic": false})
		frame := fixture.p3Frame(test, int64(index+1), fact.EventGameMS, fact)
		fixture.p3Send(test, frame)
		fixture.p3Send(test, frame)
		selected = append(selected, options[0])
	}
	for index, target := range []string{"hub_a", "hub_b"} {
		fixture.p3Send(test, fixture.p3Frame(test, int64(index+4), int64(index+4)*1000, p3Fact(player, "interact", target, int64(index+4)*1000)))
	}
	frame := fixture.p3Frame(test, 6, 6000, p3Fact(player, "kill", "boss", 6000))
	frame.Terminal = &gameplay.Terminal{Outcome: "success", Reason: "objectives_complete", Frame: 6, Sequence: 6}
	frame.Summary = &gameplay.Summary{Participants: []gameplay.Stats{{PlayerID: fmt.Sprint(player), Slot: 0, LoadoutID: "ranger", Kills: 1, Interactions: 2, Effective: true, Choices: selected}}, Reason: "none"}
	fixture.p3Send(test, frame)
	fixture.p3Send(test, frame)
	if err := fixture.app.Settlement.Settle(fixture.ctx, fixture.state.ID); err != nil {
		test.Fatal(err)
	}
	if err := fixture.app.Settlement.Settle(fixture.ctx, fixture.state.ID); err != nil {
		test.Fatal(err)
	}
	var choices string
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT choices FROM pve_dc_participant_stats WHERE run_id=? AND player_id=?", fixture.state.ID, player).Scan(&choices); err != nil {
		test.Fatal(err)
	}
	var facts, ledger int
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_run_events WHERE run_id=? AND event_type='growth_choice'", fixture.state.ID).Scan(&facts); err != nil {
		test.Fatal(err)
	}
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM asset_ledger WHERE pve_run_id=?", fixture.state.ID).Scan(&ledger); err != nil {
		test.Fatal(err)
	}
	if facts != 3 || ledger != 1 || !strings.Contains(choices, selected[2]) {
		test.Fatal("lost growth or duplicated reward", facts, ledger, choices)
	}
}

func TestP4StorageInvalidChoiceRollsBack(test *testing.T) {
	test.Setenv("DC_P4_TEST", "1")
	fixture := setup(test, 1)
	fixture.create(test, "")
	fixture.start(test)
	fact := p3Fact(fixture.players[0], "growth_choice", "runesmith_twin", 1000)
	action := store.ID("invalid_growth")
	fact.Action = &action
	fact.Proof = store.JSON(map[string]any{"node_id": "growth_1", "candidate_revision": 1, "candidate_hash": strings.Repeat("0", 64), "opened_game_ms": 0, "deadline_game_ms": 60000, "automatic": false})
	frame := fixture.p3Frame(test, 1, 1000, fact)
	if _, err := fixture.game.Frame(fixture.ctx, p3Encode(frame)); err == nil {
		test.Fatal("cross-kit accepted")
	}
	var count int
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_run_events WHERE run_id=? AND event_type='growth_choice'", fixture.state.ID).Scan(&count); err != nil || count != 0 {
		test.Fatal("rollback", err, count)
	}
}
