package run

import (
	"context"
	"database/sql"
	"fmt"
	"time"

	"game-realtime-gm/backend/internal/pve/store"
	"game-realtime-gm/backend/internal/pve/task"
)

func ApplyFrame(ctx context.Context, transaction *sql.Tx, state *State, events []Event, sequence int64, now time.Time) error {
	if state.Authority != "unity" || state.Status != "running" {
		return store.Conflict
	}
	state.batch = true
	state.batchObjectives = task.Active(state.Rules.Objectives, state.Objectives)
	state.batchPersonal = map[int64]map[string]bool{}
	deaths := map[int64]bool{}
	for _, event := range events {
		if event.EventType == "death" && event.ActorPlayerID != nil {
			deaths[*event.ActorPlayerID] = true
		}
	}
	for _, event := range events {
		if event.EventType == "interact" && event.ActorPlayerID != nil && deaths[*event.ActorPlayerID] {
			state.batch = false
			return store.Forbidden
		}
	}
	for _, member := range state.Participants {
		definition, _ := state.Rules.Task(member.TaskKey)
		state.batchPersonal[member.PlayerID] = task.Active(definition.Objectives, member.TaskProgress)
	}
	defer func() { state.batch = false; state.batchObjectives = nil; state.batchPersonal = nil }()
	for _, kindGroup := range []bool{false, true} {
		for _, event := range events {
			if (event.EventType == "death") != kindGroup {
				continue
			}
			previous := map[int64]map[string]int64{}
			contributions := map[int64]int{}
			for _, member := range state.Participants {
				previous[member.PlayerID] = map[string]int64{}
				for key, value := range member.TaskProgress {
					previous[member.PlayerID][key] = value
				}
				contributions[member.PlayerID] = member.Contribution
			}
			if err := Apply(state, event, now); err != nil {
				return err
			}
			if transaction != nil {
				if err := persistProgress(ctx, transaction, state, event, previous, contributions); err != nil {
					return err
				}
			}
		}
	}
	state.batch = false
	state.AppliedSequence = sequence
	state.ReceivedSequence = sequence
	complete := true
	for _, objective := range state.Rules.Objectives {
		complete = complete && state.Objectives[objective.Key] >= objective.Required
	}
	if complete {
		for id, spawn := range state.Spawns {
			if spawn.Status == "pending_spawn" {
				spawn.Status = "canceled"
				state.Spawns[id] = spawn
			}
		}
		finish(state, "success", sequence, now)
		return nil
	}
	for index := range state.Participants {
		member := &state.Participants[index]
		if member.Status != "participating" || member.LifeStatus != "dead" || state.ReinforcementUsed+pendingSpawns(state) >= state.ReinforcementBudget {
			continue
		}
		action := fmt.Sprintf("reinforce_%s_%d_%d", state.ID, index, member.Deaths)
		if _, exists := state.Spawns[action]; exists {
			continue
		}
		state.Spawns[action] = Spawn{PlayerID: member.PlayerID, Deaths: member.Deaths, Status: "pending_spawn", Deadline: now.Add(time.Duration(state.Rules.Spawn) * time.Second)}
		member.LifeStatus = "awaiting_respawn"
	}
	wipe(state, sequence, now)
	return nil
}

func EndAuthority(state *State, reason string, now time.Time) {
	if cooperationVersion(state.Rules.Version) {
		for id, spawn := range state.Spawns {
			if spawn.Status == "pending_spawn" {
				spawn.Status = "canceled"
				state.Spawns[id] = spawn
				member := state.Member(&spawn.PlayerID)
				if member != nil && member.LifeStatus == "awaiting_respawn" {
					member.LifeStatus = "dead"
				}
			}
		}
	}
	finish(state, reason, state.AppliedSequence, now)
}
