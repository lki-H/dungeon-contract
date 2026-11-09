package gameplay_test

import (
	"encoding/json"
	"errors"
	"fmt"
	"strings"
	"testing"

	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/store"
)

func TestV5StorageProgressTransferPreservesSourceAndCompletion(test *testing.T) {
	for _, complete := range []bool{false, true} {
		test.Run(fmt.Sprint(complete), func(test *testing.T) {
			fixture := v5Fixture(test)
			player := fixture.players[0]
			for _, entry := range []struct {
				key    string
				counts map[string]int64
			}{{"dc_hunter", map[string]int64{"kills": 7}}, {"dc_precision", map[string]int64{"hits": 50}}} {
				if _, err := fixture.db.ExecContext(fixture.ctx, "INSERT INTO pve_player_tasks(player_id,task_key,version,progress,completed,unlocked) VALUES(?,?,'dc_ruins.v4.p3.1',?,?,1)", player, entry.key, store.JSON(entry.counts), complete); err != nil {
					test.Fatal(err)
				}
			}
			preview, err := fixture.game.P3TransferPreview(fixture.ctx)
			if err != nil {
				test.Fatal("preview", err)
			}
			ownPreview := 0
			for _, entry := range preview {
				if entry["player_id"] != player {
					continue
				}
				ownPreview++
				if entry["target_version"] != "dc_tasks.v5.p4.1" {
					test.Fatal("wrong task version")
				}
			}
			if ownPreview != 2 {
				test.Fatal("own preview count", ownPreview)
			}
			for attempt := 0; attempt < 2; attempt++ {
				if err := fixture.game.TransferProgress(fixture.ctx); err != nil {
					test.Fatal(err)
				}
			}
			for _, entry := range []struct {
				key      string
				expected int64
			}{{"dc_hunter", 7}, {"dc_precision", 0}} {
				var raw []byte
				var completed bool
				if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT progress,completed FROM pve_player_tasks WHERE player_id=? AND task_key=? AND version='dc_tasks.v5.p4.1'", player, entry.key).Scan(&raw, &completed); err != nil {
					test.Fatal(err)
				}
				counts := map[string]int64{}
				if err := json.Unmarshal(raw, &counts); err != nil {
					test.Fatal(err)
				}
				key := "kills"
				expected := entry.expected
				if entry.key == "dc_precision" {
					key = "hits"
					if complete {
						expected = 50
					}
				}
				if counts[key] != expected || completed != complete {
					test.Fatal("mapped progress", entry.key, counts, completed)
				}
			}
			var mappings, rewards, source int
			if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_dc_task_transfers WHERE player_id=? AND target_version='dc_tasks.v5.p4.1'", player).Scan(&mappings); err != nil {
				test.Fatal(err)
			}
			if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_reward_grants WHERE player_id=?", player).Scan(&rewards); err != nil {
				test.Fatal(err)
			}
			if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_player_tasks WHERE player_id=? AND version='dc_ruins.v4.p3.1'", player).Scan(&source); err != nil {
				test.Fatal(err)
			}
			if mappings != 2 || rewards != 0 || source != 2 {
				test.Fatal("transfer duplicated, rewarded or changed source", mappings, rewards, source)
			}
		})
	}
}

func v5Fixture(test *testing.T) *fixture {
	test.Helper()
	test.Setenv("DC_V5_TEST", "1")
	return setup(test, 1)
}

func TestV5StorageThreeChoicesAndReliableSettlement(test *testing.T) {
	fixture := v5Fixture(test)
	fixture.create(test, "")
	fixture.start(test)
	player := fixture.players[0]
	selected := []string{}
	for index := 0; index < 3; index++ {
		node := fmt.Sprintf("growth_%d", index+1)
		options := fixture.game.Content.Candidates(fixture.manifest.Seed, node, "ranger", selected)
		choice := p3Fact(player, "growth_choice", options[0], int64(index+1)*1000)
		action := store.ID("growth")
		choice.Action = &action
		choice.Proof = store.JSON(map[string]any{"node_id": node, "candidate_revision": index + 1, "candidate_hash": store.Hash([]byte(strings.Join(options, "|"))), "opened_game_ms": int64(index) * 1000, "deadline_game_ms": int64(index)*1000 + 60000, "automatic": false})
		frame := fixture.p3Frame(test, int64(index+1), choice.EventGameMS, choice)
		fixture.p3Send(test, frame)
		fixture.p3Send(test, frame)
		selected = append(selected, options[0])
	}
	for index, target := range []string{"hub_a", "hub_b"} {
		fixture.p3Send(test, fixture.p3Frame(test, int64(index+4), int64(index+4)*1000, p3Fact(player, "interact", target, int64(index+4)*1000)))
	}
	kill := p3Fact(player, "kill", "boss", 6000)
	kill.SpawnOrigin = "boss_body"
	frame := fixture.p3Frame(test, 6, 6000, kill)
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
	var choices, grants int
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_run_events WHERE run_id=? AND event_type='growth_choice'", fixture.state.ID).Scan(&choices); err != nil || choices != 3 {
		test.Fatal("v5 choices", choices, err)
	}
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_reward_grants WHERE run_id=?", fixture.state.ID).Scan(&grants); err != nil {
		test.Fatal(err)
	}
	if grants != 1 {
		test.Fatal("v5 duplicate base grant", grants)
	}
}

func TestV5StorageSummonRejectionAndRootActionDedup(test *testing.T) {
	fixture := v5Fixture(test)
	player := fixture.players[0]
	if _, err := fixture.db.ExecContext(fixture.ctx, "INSERT INTO pve_player_tasks(player_id,task_key,version,progress,completed,unlocked) VALUES(?,'dc_precision','dc_tasks.v5.p4.1','{}',0,1)", player); err != nil {
		test.Fatal(err)
	}
	fixture.create(test, "dc_precision")
	fixture.start(test)
	damage := p3Fact(player, "damage", "summon_elite_000001", 1000)
	damage.Amount = 12
	damage.SpawnOrigin = "boss_summon"
	bad := fixture.p3Frame(test, 1, 1000, damage)
	if _, err := fixture.game.Frame(fixture.ctx, p3Encode(bad)); !errors.Is(err, store.Forbidden) {
		test.Fatal("summon accepted", err)
	}
	damage.Target = "guard_000001"
	damage.SpawnOrigin = "main_wave"
	fixture.p3Send(test, fixture.p3Frame(test, 1, 1000, damage))
	damage.EventID = uuid()
	damage.Target = "guard_000002"
	damage.EventGameMS = 2000
	fixture.p3Send(test, fixture.p3Frame(test, 2, 2000, damage))
	state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.Participants[0].TaskProgress["hits"] != 1 {
		test.Fatal("root counted twice", err)
	}
	root := store.ID("new_root")
	damage.Action = &root
	damage.EventID = uuid()
	damage.EventGameMS = 3000
	fixture.p3Send(test, fixture.p3Frame(test, 3, 3000, damage))
	state, err = fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.Participants[0].TaskProgress["hits"] != 2 {
		test.Fatal("distinct root missing", err)
	}
}
