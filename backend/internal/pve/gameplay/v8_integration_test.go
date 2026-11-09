package gameplay_test

import (
	"encoding/json"
	"errors"
	"strconv"
	"testing"

	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/store"
)

func TestV8StorageAdmissionAndReliableIdempotency(t *testing.T) {
	t.Setenv("DC_V8_TEST", "1")
	t.Setenv("DC_ENGINEERING_RUN", "1")
	fixture := setup(t, 2)
	fixture.create(t, "dc_hunter")
	fixture.start(t)
	if fixture.manifest.Versions.Protocol != 8 || fixture.manifest.Versions.World != "dc_rooms.v8.p5.1" {
		t.Fatal("wrong v8 world binding")
	}
	phaseFact := phase()
	phaseFact.EventGameMS = 1000
	frame := fixture.p3Frame(t, 1, 1000, phaseFact)
	first := fixture.p3Send(t, frame)
	second := fixture.p3Send(t, frame)
	if first.Applied != 1 || second.Applied != 1 {
		t.Fatal("replayed v8 frame")
	}
	conflicting := *frame
	conflicting.ID = uuid()
	if _, err := fixture.game.Frame(fixture.ctx, p3Encode(&conflicting)); !errors.Is(err, store.Conflict) {
		t.Fatal("changed v8 sequence accepted", err)
	}
	stale := fixture.p3Frame(t, 2, 2000, phase())
	stale.Generation++
	if _, err := fixture.game.Frame(fixture.ctx, p3Encode(stale)); err == nil {
		t.Fatal("foreign v8 source generation accepted")
	}
	task, ok := fixture.game.Content.Rules.Task("dc_precision")
	if !ok || task.Version != "dc_tasks.v5.p4.1" {
		t.Fatal("contract semantic identity")
	}
	actor := strconv.FormatInt(fixture.players[0], 10)
	damage := p3Fact(fixture.players[0], "damage", "guard_000001", 2000)
	damage.Actor = &actor
	damage.Amount = 12
	damage.SpawnOrigin = "main_wave"
	damage.Proof = json.RawMessage(`{"origin_life_generation":1,"emission_frame":2}`)
	damageFrame := fixture.p3Frame(t, 2, 2000, damage)
	fixture.p3Send(t, damageFrame)
	fixture.p3Send(t, damageFrame)
	var count, total int64
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_run_events WHERE run_id=? AND event_id=?", fixture.state.ID, damage.EventID).Scan(&count); err != nil || count != 1 {
		t.Fatal("replayed v8 damage persisted twice", count, err)
	}
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT CAST(JSON_UNQUOTE(JSON_EXTRACT(state,?)) AS SIGNED) FROM pve_dc_allocations WHERE run_id=?", `$.stats."`+actor+`".damage`, fixture.state.ID).Scan(&total); err != nil || total != 12 {
		t.Fatal("replayed v8 damage changed contribution", total, err)
	}
}

func TestV8StorageRejectsSummonAndOldBattleProtocol(t *testing.T) {
	t.Setenv("DC_V8_TEST", "1")
	t.Setenv("DC_ENGINEERING_RUN", "1")
	fixture := setup(t, 1)
	fixture.create(t, "dc_hunter")
	fixture.start(t)
	hit := p3Fact(fixture.players[0], "damage", "summon_guard_000001", 1000)
	hit.Amount = 12
	hit.SpawnOrigin = "boss_summon"
	frame := fixture.p3Frame(t, 1, 1000, hit)
	if _, err := fixture.game.ParseFrame(p3Encode(frame)); err == nil {
		t.Fatal("v8 summon progress accepted")
	}
	hit.Target = "guard_000001"
	hit.SpawnOrigin = "main_wave"
	frame.Facts = []gameplay.Fact{hit}
	frame.Schema = 7
	if _, err := fixture.game.ParseFrame(p3Encode(frame)); err == nil {
		t.Fatal("protocol7 frame accepted by v8")
	}
}

func TestV8StorageTerminalRewardDeduplicates(t *testing.T) {
	t.Setenv("DC_V8_TEST", "1")
	t.Setenv("DC_ENGINEERING_RUN", "1")
	fixture := setup(t, 1)
	fixture.create(t, "")
	fixture.start(t)
	player := fixture.players[0]
	for index, target := range []string{"hub_a", "hub_b"} {
		stamp := int64(index+1) * 1000
		fixture.p3Send(t, fixture.p3Frame(t, int64(index+1), stamp, p3Fact(player, "interact", target, stamp)))
	}
	kill := p3Fact(player, "kill", "boss", 3000)
	kill.SpawnOrigin = "boss_body"
	frame := fixture.p3Frame(t, 3, 3000, kill)
	frame.Terminal = &gameplay.Terminal{Outcome: "success", Reason: "objectives_complete", Frame: 3, Sequence: 3}
	frame.Summary = &gameplay.Summary{Participants: []gameplay.Stats{{PlayerID: strconv.FormatInt(player, 10), Slot: 0, LoadoutID: "ranger", Kills: 1, Interactions: 2, Effective: true, Choices: []string{}}}, Reason: "none"}
	fixture.p3Send(t, frame)
	fixture.p3Send(t, frame)
	for repeat := 0; repeat < 2; repeat++ {
		if err := fixture.app.Settlement.Settle(fixture.ctx, fixture.state.ID); err != nil {
			t.Fatal(err)
		}
	}
	var total, grants, ledger int64
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COALESCE(SUM(amount),0),COUNT(*) FROM pve_reward_grants WHERE run_id=?", fixture.state.ID).Scan(&total, &grants); err != nil {
		t.Fatal(err)
	}
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM asset_ledger WHERE pve_run_id=?", fixture.state.ID).Scan(&ledger); err != nil {
		t.Fatal(err)
	}
	if total != 100 || grants != 1 || ledger != 1 {
		t.Fatal("replayed v8 terminal duplicated reward or ledger", total, grants, ledger)
	}
	if _, err := fixture.game.Contributions(fixture.ctx, fixture.state.ID, player+99999999); !errors.Is(err, store.Forbidden) {
		t.Fatal("outsider received v8 result", err)
	}
}
