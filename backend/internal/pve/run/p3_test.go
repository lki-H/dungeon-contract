package run

import (
	"context"
	"fmt"
	"testing"
	"time"

	"game-realtime-gm/backend/internal/pve/store"
	"game-realtime-gm/backend/internal/pve/task"
)

func p3State(count int) *State {
	state := &State{ID: "run_p3", Authority: "unity", Status: "running", Rules: task.Rules{Version: "dc_ruins.v4.p3.1", Spawn: 10, Objectives: []task.Objective{{Key: "boss", Type: "kill", TargetID: "boss", Scope: "team", Required: 1}}}, ReinforcementBudget: 3, Spawns: map[string]Spawn{}, Facts: map[string]bool{}, Objectives: map[string]int64{}}
	for index := 0; index < count; index++ {
		state.Participants = append(state.Participants, Participant{PlayerID: int64(index + 1), Status: "participating", LifeStatus: "alive", LifeGeneration: 1, Connected: true, TaskProgress: map[string]int64{}})
	}
	return state
}
func p3Event(kind string, actor int64, game int64) Event {
	amount := int64(1)
	if kind == "downed" || kind == "death" {
		amount = 0
	}
	proof := LifeProof{Life: 1, Episode: 1, Game: game, Frame: game/33 + 1, Action: "action_p3", Amount: amount}
	return Event{EventID: store.ID("event"), EventType: kind, ActorPlayerID: &actor, TargetID: fmt.Sprint(actor), Payload: store.JSON(proof), Sequence: 1}
}
func p3Apply(state *State, events ...Event) error {
	return ApplyP3Frame(context.Background(), nil, state, events, state.AppliedSequence+1, time.Now().UTC())
}

func TestP3DownedRescueAndStaleEpisode(test *testing.T) {
	state := p3State(2)
	if err := p3Apply(state, p3Event("downed", 2, 1000)); err != nil {
		test.Fatal(err)
	}
	if len(state.Spawns) != 0 || state.Participants[1].Deaths != 0 {
		test.Fatal("downed spent reinforcement or forged death")
	}
	rescue := p3Event("rescued", 1, 5000)
	rescue.Payload = store.JSON(LifeProof{Life: 1, Episode: 1, Game: 5000, Frame: 152, Target: 2, TargetLife: 1, Started: 2000, Distance: 1500, Amount: 30, Action: "rescue_1"})
	if err := p3Apply(state, rescue); err != nil {
		test.Fatal(err)
	}
	if state.Participants[0].Rescues != 1 || state.Participants[1].LifeStatus != "alive" || state.Participants[1].Knockdowns != 1 || state.ReinforcementUsed != 0 {
		test.Fatal("rescue state")
	}
	if err := p3Apply(state, rescue); err == nil {
		test.Fatal("replayed rescue")
	}
}

func TestP3SameFrameVictoryRetainsDownAndDeathWithoutSpawn(test *testing.T) {
	state := p3State(1)
	kill := p3Event("kill", 1, 1000)
	kill.TargetID = "boss"
	if err := p3Apply(state, p3Event("downed", 1, 1000), kill, p3Event("death", 1, 1000)); err != nil {
		test.Fatal(err)
	}
	if state.EndReason != "success" || state.Participants[0].Knockdowns != 1 || state.Participants[0].Deaths != 1 || len(state.Spawns) != 0 {
		test.Fatalf("wrong terminal %+v", state)
	}
}

func TestP3LastReinforcementUsesDeathFrameThenSlot(test *testing.T) {
	state := p3State(4)
	state.ReinforcementUsed = 2
	events := []Event{}
	for actor := int64(1); actor <= 4; actor++ {
		events = append(events, p3Event("downed", actor, 1000))
	}
	for actor := int64(4); actor >= 1; actor-- {
		events = append(events, p3Event("death", actor, 1000))
	}
	if err := p3Apply(state, events...); err != nil {
		test.Fatal(err)
	}
	if len(state.Spawns) != 1 || state.Participants[0].LifeStatus != "awaiting_respawn" || state.ReinforcementUsed != 2 {
		test.Fatal("reservation order or consumption")
	}
	for action := range state.Spawns {
		spawn := p3Event("reinforcement.spawned", 1, 4000)
		spawn.TargetID = action
		if err := p3Apply(state, spawn); err != nil {
			test.Fatal(err)
		}
	}
	if state.ReinforcementUsed != 3 || state.Participants[0].LifeGeneration != 2 {
		test.Fatal("spawn did not consume once")
	}
}

func TestP3NoRecoveryWipeAndLateEmission(test *testing.T) {
	state := p3State(2)
	state.ReinforcementBudget = 0
	events := []Event{p3Event("downed", 1, 1000), p3Event("downed", 2, 1000), p3Event("death", 1, 1000), p3Event("death", 2, 1000)}
	if err := p3Apply(state, events...); err != nil {
		test.Fatal(err)
	}
	if state.EndReason != "team_wipe" {
		test.Fatal("waited for turret")
	}
	state = p3State(2)
	state.Participants[0].LifeStatus = "dead"
	damage := p3Event("damage", 1, 3000)
	damage.TargetID = "enemy_1"
	if err := p3Apply(state, damage); err != nil {
		test.Fatal("legal delayed damage", err)
	}
	state = p3State(1)
	if err := p3Apply(state, p3Event("downed", 1, 1000), p3Event("death", 1, 1000)); err != nil {
		test.Fatal(err)
	}
	for action := range state.Spawns {
		failed := p3Event("reinforcement.failed", 1, 4000)
		failed.TargetID = action
		if err := p3Apply(state, failed); err != nil {
			test.Fatal(err)
		}
	}
	if state.EndReason != "technical_failure" || pendingSpawns(state) != 0 || state.ReinforcementUsed != 0 {
		test.Fatal("failed landing waited indefinitely or spent budget")
	}
}

func TestP3InvalidLifeTransitions(test *testing.T) {
	for _, kind := range []string{"death", "rescued", "reinforcement.spawned", "protection", "control_effect"} {
		test.Run(kind, func(test *testing.T) {
			state := p3State(2)
			event := p3Event(kind, 1, 1000)
			event.Payload = store.JSON(LifeProof{Life: 2, Game: 1000, Frame: 31})
			if err := p3Apply(state, event); err == nil {
				test.Fatal("stale life accepted")
			}
		})
	}
}

func TestP3ActualUnityDamagePayload(test *testing.T) {
	state := p3State(1)
	event := p3Event("damage", 1, 6896)
	event.TargetID = "guard_000044"
	event.Payload = []byte(`{"action_id":"shot_000052","effective_amount":12,"origin_life_generation":1,"emission_frame":204,"life_generation":1,"downed_generation":0,"event_game_ms":6896,"world_frame_id":207}`)
	if err := p3Apply(state, event); err != nil {
		test.Fatal(err)
	}
}
