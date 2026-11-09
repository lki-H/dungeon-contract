package run

import (
	"context"
	"game-realtime-gm/backend/internal/pve/task"
	"testing"
	"time"
)

func TestFrameSuccessPreservesDeathAndDoesNotUnlockSameFrameTargets(test *testing.T) {
	player := int64(1)
	rules := task.Rules{Objectives: []task.Objective{{Key: "kill", Type: "kill", TargetID: "boss", Required: 1, Scope: "team"}}, Tasks: []task.Definition{{Key: "survive", NoDeaths: true, RequiresSuccess: true, Objectives: []task.Objective{{Key: "hits", Type: "damage", Required: 1, Scope: "self"}}}}}
	state := &State{ID: "run_frame", Authority: "unity", Status: "running", Rules: rules, ReinforcementBudget: 3, Facts: map[string]bool{}, Spawns: map[string]Spawn{}, Objectives: map[string]int64{}, Participants: []Participant{{PlayerID: player, Status: "participating", LifeStatus: "alive", TaskKey: "survive", Compatibility: "compatible", TaskProgress: map[string]int64{}}}}
	events := []Event{{EventType: "death", ActorPlayerID: &player}, {EventType: "kill", ActorPlayerID: &player, TargetID: "boss"}}
	if err := ApplyFrame(context.Background(), nil, state, events, 1, time.Now()); err != nil {
		test.Fatal(err)
	}
	if state.EndReason != "success" || state.Participants[0].Deaths != 1 || len(state.Spawns) != 0 {
		test.Fatal("same-frame outcome or reinforcement wrong")
	}
	state = &State{ID: "run_chain", Authority: "unity", Status: "running", Rules: task.Rules{Objectives: []task.Objective{{Key: "first", Type: "interact", Required: 1, TargetID: "hub", Scope: "team"}, {Key: "next", Type: "reach", Required: 1, TargetID: "exit", Scope: "team", DependsOn: []string{"first"}}}}, Facts: map[string]bool{}, Spawns: map[string]Spawn{}, Objectives: map[string]int64{}, Participants: []Participant{{PlayerID: player, Status: "participating", LifeStatus: "alive"}}}
	if err := ApplyFrame(context.Background(), nil, state, []Event{{EventType: "interact", ActorPlayerID: &player, TargetID: "hub"}, {EventType: "reach", ActorPlayerID: &player, TargetID: "exit"}}, 1, time.Now()); err != nil {
		test.Fatal(err)
	}
	if state.Objectives["next"] != 0 || state.Status != "running" {
		test.Fatal("same-frame new target advanced")
	}
}
