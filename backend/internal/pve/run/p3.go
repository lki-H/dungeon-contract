package run

import (
	"context"
	"database/sql"
	"encoding/json"
	"fmt"
	"sort"
	"time"

	"game-realtime-gm/backend/internal/pve/store"
	"game-realtime-gm/backend/internal/pve/task"
)

type LifeProof struct {
	Life       int64  `json:"life_generation"`
	Episode    int64  `json:"downed_generation"`
	Game       int64  `json:"event_game_ms"`
	Frame      int64  `json:"world_frame_id"`
	Target     int64  `json:"target_player_id,string,omitempty"`
	TargetLife int64  `json:"target_life_generation"`
	Started    int64  `json:"started_game_ms"`
	Distance   int64  `json:"distance_milli"`
	Amount     int64  `json:"effective_amount"`
	Action     string `json:"action_id"`
	Reason     string `json:"reason"`
}

func ApplyP3Frame(ctx context.Context, transaction *sql.Tx, state *State, events []Event, sequence int64, now time.Time) error {
	if !cooperationVersion(state.Rules.Version) || state.Status != "running" || state.Authority != "unity" {
		return store.Conflict
	}
	state.batch = true
	state.batchObjectives = task.Active(state.Rules.Objectives, state.Objectives)
	state.batchPersonal = map[int64]map[string]bool{}
	defer func() { state.batch = false; state.batchObjectives = nil; state.batchPersonal = nil }()
	for _, member := range state.Participants {
		definition, _ := state.Rules.Task(member.TaskKey)
		state.batchPersonal[member.PlayerID] = task.Active(definition.Objectives, member.TaskProgress)
	}
	spawnFailed := false
	for _, event := range events {
		member := state.Member(event.ActorPlayerID)
		if member == nil || member.Status != "participating" {
			return store.Forbidden
		}
		if member.LifeGeneration == 0 {
			member.LifeGeneration = 1
		}
		previous := map[int64]map[string]int64{}
		contributions := map[int64]int{}
		for _, participant := range state.Participants {
			previous[participant.PlayerID] = map[string]int64{}
			for key, count := range participant.TaskProgress {
				previous[participant.PlayerID][key] = count
			}
			contributions[participant.PlayerID] = participant.Contribution
		}
		var proof LifeProof
		if json.Unmarshal(event.Payload, &proof) != nil || proof.Life < 1 || proof.Game < 0 || proof.Frame < 1 {
			return store.Invalid
		}
		if proof.Life != member.LifeGeneration {
			return store.Conflict
		}
		key := event.EventType + ":" + event.TargetID
		switch event.EventType {
		case "downed":
			if member.LifeStatus != "alive" || proof.Episode != member.DownedGeneration+1 || event.TargetID != fmt.Sprint(member.PlayerID) || proof.Amount != 0 {
				return store.Conflict
			}
			member.LifeStatus = "downed"
			member.Knockdowns++
			member.DownedGeneration = proof.Episode
			member.DownedAt = proof.Game
		case "death":
			if member.LifeStatus != "downed" || proof.Episode != member.DownedGeneration || event.TargetID != fmt.Sprint(member.PlayerID) || proof.Game < member.DownedAt || proof.Amount != 0 {
				return store.Conflict
			}
			if proof.Game-member.DownedAt < 20000 {
				for _, other := range state.Participants {
					if other.Status == "participating" && other.LifeStatus == "alive" && other.Connected {
						return store.Conflict
					}
				}
			}
			member.LifeStatus = "dead"
			member.Deaths++
			member.DeathFrame = proof.Frame
		case "rescued":
			target := state.Member(&proof.Target)
			if !member.Connected || member.LifeStatus != "alive" || target == nil || target == member || target.Status != "participating" || target.LifeStatus != "downed" || target.LifeGeneration != proof.TargetLife || target.DownedGeneration != proof.Episode || proof.Distance < 0 || proof.Distance > 1500 || proof.Started < target.DownedAt || proof.Game-proof.Started < 3000 || proof.Game >= target.DownedAt+20000 || proof.Amount != 30 || !store.ValidID(proof.Action) {
				return store.Conflict
			}
			key = fmt.Sprintf("rescued:%d:%d:%d", target.PlayerID, target.LifeGeneration, target.DownedGeneration)
			if state.Facts[key] {
				return store.Conflict
			}
			state.Facts[key] = true
			target.LifeStatus = "alive"
			member.Rescues++
			member.Contribution++
		case "protection", "control_effect", "projectile_cleared":
			if proof.Amount <= 0 || proof.Amount > 1000000000 || !store.ValidID(proof.Action) {
				return store.Invalid
			}
			key = event.EventType + ":" + proof.Action + ":" + event.TargetID
			if state.Facts[key] {
				return store.Conflict
			}
			state.Facts[key] = true
			if event.EventType == "protection" {
				target := state.Member(&proof.Target)
				if target == nil || target == member || target.Status != "participating" {
					return store.Forbidden
				}
				member.ProtectedDamage += proof.Amount
			} else if event.EventType == "projectile_cleared" {
				target := state.Member(&proof.Target)
				if target == nil || target.Status != "participating" || proof.Amount != 1 {
					return store.Forbidden
				}
				member.ClearedProjectiles++
			} else {
				member.ControlEffect += proof.Amount
			}
			member.Contribution++
		case "reinforcement.spawned", "reinforcement.failed":
			spawn, exists := state.Spawns[event.TargetID]
			if !exists || spawn.PlayerID != member.PlayerID || spawn.Deaths != member.Deaths || spawn.Status != "pending_spawn" || member.LifeStatus != "awaiting_respawn" {
				return store.Conflict
			}
			if event.EventType == "reinforcement.spawned" {
				spawn.Status = "spawned"
				member.LifeStatus = "alive"
				member.LifeGeneration++
				state.ReinforcementUsed++
			} else {
				spawn.Status = "failed"
				member.LifeStatus = "dead"
				spawnFailed = true
			}
			state.Spawns[event.TargetID] = spawn
		default:
			if err := Apply(state, event, now); err != nil {
				return err
			}
		}
		if transaction != nil {
			if err := persistProgress(ctx, transaction, state, event, previous, contributions); err != nil {
				return err
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
				member := state.Member(&spawn.PlayerID)
				if member != nil && member.LifeStatus == "awaiting_respawn" {
					member.LifeStatus = "dead"
				}
			}
		}
		finish(state, "success", sequence, now)
		return nil
	}
	if spawnFailed {
		EndAuthority(state, "technical_failure", now)
		return nil
	}
	candidates := []*Participant{}
	for index := range state.Participants {
		member := &state.Participants[index]
		if member.Status == "participating" && member.LifeStatus == "dead" {
			candidates = append(candidates, member)
		}
	}
	sort.SliceStable(candidates, func(first, second int) bool { return candidates[first].DeathFrame < candidates[second].DeathFrame })
	for _, member := range candidates {
		if state.ReinforcementUsed+pendingSpawns(state) >= state.ReinforcementBudget {
			break
		}
		slot := 0
		for index := range state.Participants {
			if state.Participants[index].PlayerID == member.PlayerID {
				slot = index
				break
			}
		}
		action := fmt.Sprintf("reinforce_%s_%d_%d", state.ID, slot, member.Deaths)
		if _, exists := state.Spawns[action]; exists {
			continue
		}
		state.Spawns[action] = Spawn{PlayerID: member.PlayerID, Deaths: member.Deaths, Status: "pending_spawn", Deadline: now.Add(time.Duration(state.Rules.Spawn) * time.Second)}
		member.LifeStatus = "awaiting_respawn"
	}
	wipe(state, sequence, now)
	return nil
}
