package gameplay_test

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"strconv"
	"testing"
	"time"

	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/store"
	"game-realtime-gm/backend/internal/pve/worker"
)

func p3Fixture(test *testing.T, count int) *fixture {
	test.Setenv("DC_P3_TEST", "1")
	return setup(test, count)
}
func p3Fact(player int64, kind, target string, game int64) gameplay.Fact {
	value := fact(player, kind, target)
	value.LifeGeneration = 1
	value.EventGameMS = game
	value.Proof = json.RawMessage(`{}`)
	return value
}
func (fixture *fixture) p3Frame(test *testing.T, sequence, game int64, facts ...gameplay.Fact) *gameplay.Frame {
	test.Helper()
	if fixture.state.StartedAt == nil {
		state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
		if err != nil {
			test.Fatal(err)
		}
		fixture.state = state
	}
	fixture.clock = fixture.state.StartedAt.Add(time.Duration(game) * time.Millisecond)
	if err := fixture.game.Register(fixture.ctx, gameplay.InstanceRequest{Versions: fixture.game.Content.V3, InstanceID: "dc_local_01", BootID: fixture.manifest.BootID, ProtocolVersion: fixture.game.Content.ProtocolVersion(), ConfigVersions: []gameplay.ConfigVersion{{ID: fixture.game.Content.Rules.Version, Hash: fixture.game.Content.Hash}}}); err != nil {
		test.Fatal(err)
	}
	frame := fixture.frame(sequence, facts)
	frame.Source = "unity"
	frame.Elapsed = game
	frame.WallElapsed = game
	return frame
}
func p3Encode(frame *gameplay.Frame) []byte {
	value, _ := gameplay.DecodeJSON(encode(frame))
	object := value.(map[string]any)
	object["manual_pause_ms"] = json.Number(strconv.FormatInt(frame.ManualPause, 10))
	object["wall_elapsed_ms"] = json.Number(strconv.FormatInt(frame.WallElapsed, 10))
	delete(object, "fingerprint")
	object["fingerprint"] = store.Hash(gameplay.Canonical(object))
	return store.JSON(object)
}
func (fixture *fixture) p3Send(test *testing.T, frame *gameplay.Frame) *gameplay.Receipt {
	test.Helper()
	data := p3Encode(frame)
	if _, err := fixture.game.ParseFrame(data); err != nil {
		value, _ := gameplay.DecodeJSON(data)
		test.Fatal("P3 frame contract", err, gameplay.ValidateSchema(value, fixture.game.Content.FrameSchema))
	}
	receipt, err := fixture.game.Frame(fixture.ctx, data)
	if err != nil {
		test.Fatal("P3 fact application", err)
	}
	return receipt
}

func TestP3StorageSuccessDeathAndSettlementIdempotency(test *testing.T) {
	fixture := p3Fixture(test, 1)
	fixture.create(test, "dc_survivor")
	fixture.start(test)
	player := fixture.players[0]
	for index, target := range []string{"hub_a", "hub_b"} {
		fixture.p3Send(test, fixture.p3Frame(test, int64(index+1), int64(index+1)*1000, p3Fact(player, "interact", target, int64(index+1)*1000)))
	}
	down := p3Fact(player, "downed", fmt.Sprint(player), 3000)
	down.DownedGeneration = 1
	death := p3Fact(player, "death", fmt.Sprint(player), 3000)
	death.DownedGeneration = 1
	frame := fixture.p3Frame(test, 3, 3000, down, p3Fact(player, "kill", "boss", 3000), death)
	frame.Terminal = &gameplay.Terminal{Outcome: "success", Reason: "objectives_complete", Frame: 3, Sequence: 3}
	frame.Summary = &gameplay.Summary{Participants: []gameplay.Stats{{PlayerID: fmt.Sprint(player), Slot: 0, LoadoutID: "ranger", Kills: 1, Interactions: 2, Deaths: 1, Knockdowns: 1, Effective: true, Choices: []string{}}}, Reason: "none"}
	fixture.p3Send(test, frame)
	fixture.p3Send(test, frame)
	state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.EndReason != "success" || state.Participants[0].Knockdowns != 1 || state.Participants[0].Deaths != 1 || len(state.Spawns) != 0 {
		test.Fatal("same frame outcome", err)
	}
	failedOnce := false
	recovery := &worker.Worker{DB: fixture.db, Settle: func(ctx context.Context, id string) error {
		if id == state.ID && !failedOnce {
			failedOnce = true
			return store.Conflict
		}
		return fixture.app.Settlement.Settle(ctx, id)
	}}
	if err := recovery.Once(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	if !failedOnce {
		test.Fatal("terminal did not enqueue durable Worker work")
	}
	time.Sleep(1100 * time.Millisecond)
	if err := recovery.Once(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	for repeat := 0; repeat < 2; repeat++ {
		if err := fixture.app.Settlement.Settle(fixture.ctx, state.ID); err != nil {
			test.Fatal(err)
		}
	}
	var total, grants, ledger int64
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COALESCE(SUM(amount),0),COUNT(*) FROM pve_reward_grants WHERE run_id=?", state.ID).Scan(&total, &grants); err != nil {
		test.Fatal(err)
	}
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM asset_ledger WHERE pve_run_id=?", state.ID).Scan(&ledger); err != nil {
		test.Fatal(err)
	}
	if total != 100 || grants != 1 || ledger != 1 {
		test.Fatal("duplicate reward or false survivor", total, grants, ledger)
	}
	cards, err := fixture.game.Contributions(fixture.ctx, state.ID, player)
	if err != nil || len(cards) != 1 || fmt.Sprint(cards[0]["knockdowns"]) != "1" {
		test.Fatal("life result card", err)
	}
	if _, err := fixture.game.Contributions(fixture.ctx, state.ID, player+999999); !errors.Is(err, store.Forbidden) {
		test.Fatal("outsider result access", err)
	}
	if err := fixture.app.Projection.Redis.ZRem(fixture.ctx, "v2:rewards", fmt.Sprint(player)).Err(); err != nil {
		test.Fatal(err)
	}
	if err := fixture.app.Projection.Rebuild(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	score, err := fixture.app.Projection.Redis.ZScore(fixture.ctx, "v2:rewards", fmt.Sprint(player)).Result()
	if err != nil || score != 100 {
		test.Fatal("Redis projected false assets", err, score)
	}
}

func TestP3StorageRescueGenerationAndPrivacy(test *testing.T) {
	fixture := p3Fixture(test, 2)
	fixture.create(test, "dc_technician")
	fixture.start(test)
	first, second := fixture.players[0], fixture.players[1]
	down := p3Fact(second, "downed", fmt.Sprint(second), 1000)
	down.DownedGeneration = 1
	fixture.p3Send(test, fixture.p3Frame(test, 1, 1000, down))
	rescue := p3Fact(first, "rescued", fmt.Sprint(second), 5000)
	rescue.DownedGeneration = 1
	rescue.Amount = 30
	rescue.Proof = store.JSON(map[string]any{"target_player_id": fmt.Sprint(second), "target_life_generation": 1, "started_game_ms": 2000, "distance_milli": 1500})
	fixture.p3Send(test, fixture.p3Frame(test, 2, 5000, rescue))
	state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.Participants[0].Rescues != 1 || state.Participants[1].Deaths != 0 || len(state.Spawns) != 0 {
		test.Fatal("rescue budget", err)
	}
	view := state.ForPlayer(first)
	if view.Participants[1].TaskKey != "" || view.Participants[1].TaskProgress != nil || view.Participants[1].TaskCategory != "mechanism" || view.Participants[0].TaskKey != "dc_technician" {
		test.Fatal("contract privacy")
	}
	replay := fixture.p3Frame(test, 3, 5000, rescue)
	replay.Facts[0].EventID = uuid()
	if _, err := fixture.game.Frame(fixture.ctx, p3Encode(replay)); !errors.Is(err, store.Conflict) {
		test.Fatal("duplicate rescue accepted", err)
	}
}

func TestP3StorageFourDeathsReserveOnlyThreeBySlot(test *testing.T) {
	fixture := p3Fixture(test, 4)
	fixture.create(test, "")
	fixture.start(test)
	facts := []gameplay.Fact{}
	for _, player := range fixture.players {
		down := p3Fact(player, "downed", fmt.Sprint(player), 1000)
		down.DownedGeneration = 1
		facts = append(facts, down)
	}
	for index := 3; index >= 0; index-- {
		death := p3Fact(fixture.players[index], "death", fmt.Sprint(fixture.players[index]), 1000)
		death.DownedGeneration = 1
		facts = append(facts, death)
	}
	receipt := fixture.p3Send(test, fixture.p3Frame(test, 1, 1000, facts...))
	if len(receipt.Reinforcements) != 3 {
		test.Fatal("wrong team budget")
	}
	for _, spawn := range receipt.Reinforcements {
		if spawn.PlayerID == fixture.players[3] {
			test.Fatal("packet order determined last slot")
		}
	}
	spawns := []gameplay.Fact{}
	for action, spawn := range receipt.Reinforcements {
		value := p3Fact(spawn.PlayerID, "reinforcement_spawned", strconv.FormatInt(spawn.PlayerID, 10), 4000)
		value.DownedGeneration = 1
		value.Reinforcement = action
		spawns = append(spawns, value)
	}
	fixture.p3Send(test, fixture.p3Frame(test, 2, 4000, spawns...))
	state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.ReinforcementUsed != 3 || state.Participants[0].LifeGeneration != 2 || state.Participants[3].LifeStatus != "dead" {
		test.Fatal("actual spawn consumption", err)
	}
}

func TestP3StorageProgressTransferPreservesCompletionWithoutRewards(test *testing.T) {
	fixture := p3Fixture(test, 1)
	player := fixture.players[0]
	for _, key := range []string{"dc_hunter", "dc_survivor"} {
		if _, err := fixture.db.ExecContext(fixture.ctx, "INSERT INTO pve_player_tasks(player_id,task_key,version,progress,completed,unlocked) VALUES(?,?,'dc_ruins.v2',?,0,1)", player, key, store.JSON(map[string]int64{"kills": 5, "hits": 12})); err != nil {
			test.Fatal(err)
		}
	}
	if _, err := fixture.db.ExecContext(fixture.ctx, "INSERT INTO pve_player_tasks(player_id,task_key,version,progress,completed,unlocked) VALUES(?,'dc_precision','dc_ruins.v2','{}',1,1)", player); err != nil {
		test.Fatal(err)
	}
	for repeat := 0; repeat < 2; repeat++ {
		if err := fixture.game.TransferProgress(fixture.ctx); err != nil {
			test.Fatal(err)
		}
	}
	var data []byte
	var complete bool
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT progress FROM pve_player_tasks WHERE player_id=? AND task_key='dc_hunter' AND version='dc_ruins.v4.p3.1'", player).Scan(&data); err != nil {
		test.Fatal(err)
	}
	counts := map[string]int64{}
	if json.Unmarshal(data, &counts) != nil || counts["kills"] != 5 {
		test.Fatal("lost old count")
	}
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT progress FROM pve_player_tasks WHERE player_id=? AND task_key='dc_survivor' AND version='dc_ruins.v4.p3.1'", player).Scan(&data); err != nil || string(data) != "{}" {
		test.Fatal("no-death progress treated as no-knockdown", err, string(data))
	}
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT completed FROM pve_player_tasks WHERE player_id=? AND task_key='dc_precision' AND version='dc_ruins.v4.p3.1'", player).Scan(&complete); err != nil || !complete {
		test.Fatal("completed once lost", err)
	}
	var transfers, rewards int
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_dc_task_transfers WHERE player_id=? AND target_version='dc_ruins.v4.p3.1'", player).Scan(&transfers); err != nil {
		test.Fatal(err)
	}
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_reward_grants WHERE player_id=?", player).Scan(&rewards); err != nil {
		test.Fatal(err)
	}
	if transfers != 3 || rewards != 0 {
		test.Fatal("transfer duplicated rewards")
	}
}

func TestP3StorageFactsBeforePauseDoNotApplyClockOrLeave(test *testing.T) {
	fixture := p3Fixture(test, 1)
	fixture.create(test, "")
	fixture.start(test)
	operation := store.ID("pause")
	raw, err := fixture.app.Command(fixture.ctx, fixture.players[0], operation, "v2.run.pause", store.JSON(map[string]any{"run_id": fixture.state.ID, "reason": "menu", "expected_clock_revision": 1, "pause_id": nil}))
	if err != nil {
		test.Fatal(err)
	}
	var command struct {
		Pause string `json:"pause_id"`
	}
	if json.Unmarshal(raw, &command) != nil {
		test.Fatal("pause command")
	}
	frame := fixture.p3Frame(test, 1, 1000, p3Fact(fixture.players[0], "damage", "enemy_1", 1000))
	frame.Facts[0].Amount = 1
	frame.Control = 1
	receipt := fixture.p3Send(test, frame)
	if receipt.RunStatus != "running" {
		test.Fatal("pending pause became permanent leave")
	}
	ack := gameplay.S1Ack{Protocol: 4, Assignment: fixture.manifest.AssignmentID, Boot: fixture.manifest.BootID, Generation: fixture.manifest.Generation, Operation: operation, Control: 1, Action: "pause", Frame: 1, Game: 1000, Wall: 1000, PauseID: command.Pause, Revision: 2, Checkpoint: store.Hash([]byte("p3_pause_checkpoint"))}
	if _, err := fixture.game.ClockAck(fixture.ctx, fixture.state.ID, ack); err != nil {
		test.Fatal("facts incorrectly advanced clock watermark", err)
	}
	state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.Participants[0].Status != "participating" {
		test.Fatal("pause removed participant", err)
	}
}

func TestP3StorageDefenseEvidenceAndEnvironmentalExclusion(test *testing.T) {
	fixture := p3Fixture(test, 2)
	fixture.create(test, "dc_technician")
	fixture.start(test)
	actor, defender := fixture.players[0], fixture.players[1]
	protect := p3Fact(defender, "protection", fmt.Sprint(actor), 1000)
	protect.Amount = 12
	protect.Proof = store.JSON(map[string]any{"target_player_id": fmt.Sprint(actor)})
	fixture.p3Send(test, fixture.p3Frame(test, 1, 1000, protect))
	interact := p3Fact(actor, "interact", "hub_a", 2000)
	interact.Contributors = []string{fmt.Sprint(defender)}
	interact.Proof = store.JSON(map[string]any{"defenders": []any{map[string]any{"player_id": fmt.Sprint(defender), "kind": "protection", "action_id": *protect.Action, "room_id": 2, "event_game_ms": 1000, "distance_milli": 2000, "effective_amount": 12}}})
	fixture.p3Send(test, fixture.p3Frame(test, 2, 2000, interact))
	state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.Participants[1].TaskProgress["install"] != 1 || state.Participants[1].ProtectedDamage != 12 {
		test.Fatal("real defense qualification", err)
	}
	bad := p3Fact(actor, "interact", "hub_b", 7000)
	bad.Contributors = []string{fmt.Sprint(defender)}
	bad.Proof = interact.Proof
	frame := fixture.p3Frame(test, 3, 7000, bad)
	if _, err := fixture.game.Frame(fixture.ctx, p3Encode(frame)); err == nil {
		test.Fatal("wrong region/stale proof accepted")
	}
	empty := p3Fact(defender, "control_effect", "guard_1", 7000)
	empty.Amount = 0
	frame = fixture.p3Frame(test, 3, 7000, empty)
	if _, err := fixture.game.Frame(fixture.ctx, p3Encode(frame)); err == nil {
		test.Fatal("empty cast awarded")
	}
}

func TestP3StorageSurvivorAndHunterChain(test *testing.T) {
	for _, key := range []string{"dc_survivor", "dc_hunter"} {
		test.Run(key, func(test *testing.T) {
			fixture := p3Fixture(test, 1)
			fixture.create(test, key)
			fixture.start(test)
			player := fixture.players[0]
			sequence := int64(1)
			facts := []gameplay.Fact{}
			if key == "dc_hunter" {
				for index := 0; index < 64; index++ {
					facts = append(facts, p3Fact(player, "kill", fmt.Sprintf("guard_%d", index), 1000))
				}
			} else {
				for index := 0; index < 12; index++ {
					value := p3Fact(player, "damage", fmt.Sprintf("guard_%d", index), 1000)
					value.Amount = 8
					facts = append(facts, value)
				}
			}
			fixture.p3Send(test, fixture.p3Frame(test, sequence, 1000, facts...))
			sequence++
			for _, target := range []string{"hub_a", "hub_b"} {
				fixture.p3Send(test, fixture.p3Frame(test, sequence, sequence*1000, p3Fact(player, "interact", target, sequence*1000)))
				sequence++
			}
			frame := fixture.p3Frame(test, sequence, sequence*1000, p3Fact(player, "kill", "boss", sequence*1000))
			frame.Terminal = &gameplay.Terminal{Outcome: "success", Reason: "objectives_complete", Frame: sequence, Sequence: sequence}
			stats := gameplay.Stats{PlayerID: fmt.Sprint(player), Slot: 0, LoadoutID: "ranger", Kills: 1, Interactions: 2, Effective: true, Choices: []string{}}
			if key == "dc_hunter" {
				stats.Kills = 65
			} else {
				stats.Damage = 96
			}
			frame.Summary = &gameplay.Summary{Participants: []gameplay.Stats{stats}, Reason: "none"}
			fixture.p3Send(test, frame)
			if err := fixture.app.Settlement.Settle(fixture.ctx, fixture.state.ID); err != nil {
				test.Fatal(err)
			}
			var completed bool
			var amount int64
			if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT task_completed FROM pve_participant_results WHERE run_id=? AND player_id=?", fixture.state.ID, player).Scan(&completed); err != nil || !completed {
				test.Fatal("contract did not complete", err)
			}
			if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT SUM(amount) FROM pve_reward_grants WHERE run_id=?", fixture.state.ID).Scan(&amount); err != nil {
				test.Fatal(err)
			}
			expected := int64(145)
			if key == "dc_hunter" {
				expected = 130
				var unlocked bool
				if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT unlocked FROM pve_player_tasks WHERE player_id=? AND task_key='dc_precision' AND version='dc_ruins.v4.p3.1'", player).Scan(&unlocked); err != nil || !unlocked {
					test.Fatal("chain did not unlock", err)
				}
			}
			if amount != expected {
				test.Fatal("wrong contract reward", amount)
			}
		})
	}
}

func TestP3StorageRestartRetainsConfirmedPrefixWithoutFalseTotals(test *testing.T) {
	fixture := p3Fixture(test, 1)
	fixture.create(test, "")
	fixture.start(test)
	player := fixture.players[0]
	damage := p3Fact(player, "damage", "guard_000001", 1000)
	damage.Amount = 12
	fixture.p3Send(test, fixture.p3Frame(test, 1, 1000, damage))
	if err := fixture.app.Recover(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	if err := fixture.app.Settlement.Settle(fixture.ctx, fixture.state.ID); err != nil {
		test.Fatal(err)
	}
	cards, err := fixture.game.Contributions(fixture.ctx, fixture.state.ID, player)
	if err != nil || len(cards) != 1 || cards[0]["totals_complete"] != false {
		test.Fatal("restart forged final totals", err, cards)
	}
	state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.EndReason != "server_restart" || state.Participants[0].Contribution == 0 {
		test.Fatal("restart lost confirmed contribution", err)
	}
}

func TestP3StorageTechnicalAbortConfirmsWithoutGameplayReward(test *testing.T) {
	fixture := p3Fixture(test, 1)
	fixture.create(test, "")
	fixture.start(test)
	phaseFact := phase()
	phaseFact.EventGameMS = 1000
	frame := fixture.p3Frame(test, 1, 1000, phaseFact)
	frame.Terminal = &gameplay.Terminal{Outcome: "aborted", Reason: "technical_failure", Frame: 1, Sequence: 1}
	frame.Summary = &gameplay.Summary{Participants: []gameplay.Stats{{PlayerID: fmt.Sprint(fixture.players[0]), Slot: 0, LoadoutID: "ranger", Choices: []string{}}}, Reason: "none"}
	fixture.p3Send(test, frame)
	fixture.p3Send(test, frame)
	if err := fixture.app.Settlement.Settle(fixture.ctx, fixture.state.ID); err != nil {
		test.Fatal(err)
	}
	state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.Status != "closed" || state.EndReason != "technical_failure" {
		test.Fatal("technical abort not reliably closed", err)
	}
	var rewards int
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_reward_grants WHERE run_id=?", state.ID).Scan(&rewards); err != nil || rewards != 0 {
		test.Fatal("technical abort rewarded", err)
	}
}
