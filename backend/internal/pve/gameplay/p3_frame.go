package gameplay

import (
	"encoding/json"
	"strconv"

	"game-realtime-gm/backend/internal/pve/run"
	"game-realtime-gm/backend/internal/pve/store"
)

func (service *Service) p3EventPayload(state *run.State, fact Fact, frame Frame) (json.RawMessage, error) {
	proof := map[string]any{}
	if len(fact.Proof) > 0 && string(fact.Proof) != "null" {
		if json.Unmarshal(fact.Proof, &proof) != nil || proof == nil {
			return nil, store.Invalid
		}
	}
	proof["life_generation"] = fact.LifeGeneration
	proof["downed_generation"] = fact.DownedGeneration
	proof["event_game_ms"] = fact.EventGameMS
	proof["world_frame_id"] = frame.WorldFrame
	proof["effective_amount"] = fact.Amount
	if fact.Action != nil {
		proof["action_id"] = *fact.Action
	}
	if fact.Kind == "damage" {
		if origin, ok := proof["origin_life_generation"].(float64); ok && (origin < 1 || origin > float64(fact.LifeGeneration)) {
			return nil, store.Invalid
		}
		if emitted, ok := proof["emission_frame"].(float64); ok && (emitted < 1 || emitted > float64(frame.WorldFrame)) {
			return nil, store.Invalid
		}
	}
	if fact.Kind == "interact" {
		proofs, ok := proof["defenders"].([]any)
		if len(fact.Contributors) > 0 && !ok {
			return nil, store.Invalid
		}
		if len(proofs) != len(fact.Contributors) {
			return nil, store.Invalid
		}
		room := float64(2)
		if fact.Target == "hub_b" {
			room = 3
		} else if fact.Target != "hub_a" {
			return nil, store.Invalid
		}
		for index, id := range fact.Contributors {
			item, ok := proofs[index].(map[string]any)
			if !ok {
				return nil, store.Invalid
			}
			target, _ := strconv.ParseInt(id, 10, 64)
			member := state.Member(&target)
			if fact.Actor != nil && id == *fact.Actor {
				return nil, store.Invalid
			}
			stamp, ok := item["event_game_ms"].(float64)
			if !ok {
				return nil, store.Invalid
			}
			distance, ok := item["distance_milli"].(float64)
			if !ok {
				return nil, store.Invalid
			}
			amount, ok := item["effective_amount"].(float64)
			if !ok {
				return nil, store.Invalid
			}
			kind, _ := item["kind"].(string)
			action, _ := item["action_id"].(string)
			if member == nil || member.Status != "participating" || item["player_id"] != id || item["room_id"] != room || stamp > float64(frame.Elapsed) || float64(frame.Elapsed)-stamp > 5000 || distance < 0 || distance > 5000 || amount <= 0 || !store.ValidID(action) || (kind != "damage" && kind != "protection" && kind != "control_effect") {
				return nil, store.Forbidden
			}
		}
	} else if len(fact.Contributors) > 0 {
		return nil, store.Invalid
	}
	return store.JSON(proof), nil
}
