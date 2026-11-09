package gameplay_test

import (
	"encoding/json"
	"game-realtime-gm/backend/internal/pve/gameplay"
	"strconv"
	"testing"
)

func TestV6StorageAdmissionAndReliableIdempotency(t *testing.T) {
	t.Setenv("DC_V6_TEST", "1")
	t.Setenv("DC_ENGINEERING_RUN", "1")
	f := setup(t, 2)
	f.create(t, "dc_hunter")
	f.start(t)
	if f.manifest.Versions.Protocol != 6 || f.manifest.Versions.World != "dc_rooms.v6.p5.2" {
		t.Fatal("wrong world binding")
	}
	phase := phase()
	phase.EventGameMS = 1000
	frame := f.p3Frame(t, 1, 1000, phase)
	first := f.p3Send(t, frame)
	second := f.p3Send(t, frame)
	if first.Applied != 1 || second.Applied != 1 {
		t.Fatal("replayed frame")
	}
	stale := *frame
	stale.Generation++
	if _, e := f.game.Frame(f.ctx, p3Encode(&stale)); e == nil {
		t.Fatal("old source accepted")
	}
	task, ok := f.game.Content.Rules.Task("dc_precision")
	if !ok || task.Version != "dc_tasks.v5.p4.1" {
		t.Fatal("contract semantic identity")
	}
	actor := strconv.FormatInt(f.players[0], 10)
	damage := p3Fact(f.players[0], "damage", "guard_000001", 2000)
	damage.Actor = &actor
	damage.Amount = 12
	damage.SpawnOrigin = "main_wave"
	damage.Proof = json.RawMessage(`{"origin_life_generation":1,"emission_frame":2}`)
	f.p3Send(t, f.p3Frame(t, 2, 2000, damage))
}
func TestV6StorageRejectsSummonAndOldBattleProtocol(t *testing.T) {
	t.Setenv("DC_V6_TEST", "1")
	t.Setenv("DC_ENGINEERING_RUN", "1")
	f := setup(t, 1)
	f.create(t, "dc_hunter")
	f.start(t)
	hit := p3Fact(f.players[0], "damage", "summon_guard_000001", 1000)
	hit.Amount = 12
	hit.SpawnOrigin = "boss_summon"
	frame := f.p3Frame(t, 1, 1000, hit)
	if _, e := f.game.ParseFrame(p3Encode(frame)); e == nil {
		t.Fatal("summon progress accepted")
	}
	hit.Target = "guard_000001"
	hit.SpawnOrigin = "main_wave"
	frame.Facts = []gameplay.Fact{hit}
	frame.Schema = 5
	if _, e := f.game.ParseFrame(p3Encode(frame)); e == nil {
		t.Fatal("protocol5 frame accepted")
	}
}
