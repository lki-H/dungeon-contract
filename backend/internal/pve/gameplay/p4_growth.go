package gameplay

import (
	"encoding/json"
	"fmt"
	"slices"
	"strings"

	"game-realtime-gm/backend/internal/pve/store"
)

func (service *Service) applyP4Choice(allocation *Allocation, fact Fact, frame Frame) error {
	if !service.P4() || fact.Actor == nil || fact.Action == nil || !store.ValidID(*fact.Action) || fact.Amount != 0 || len(fact.Contributors) != 0 {
		return store.Invalid
	}
	stats, exists := allocation.Stats[*fact.Actor]
	if !exists || len(stats.Choices) >= 3 {
		return store.Conflict
	}
	var proof struct {
		Node      string `json:"node_id"`
		Revision  int    `json:"candidate_revision"`
		Hash      string `json:"candidate_hash"`
		Opened    int64  `json:"opened_game_ms"`
		Deadline  int64  `json:"deadline_game_ms"`
		Automatic bool   `json:"automatic"`
	}
	if json.Unmarshal(fact.Proof, &proof) != nil {
		return store.Invalid
	}
	proofValue, err := DecodeJSON(fact.Proof)
	if err != nil {
		return store.Invalid
	}
	proofObject, ok := proofValue.(map[string]any)
	if !ok || len(proofObject) != 6 {
		return store.Invalid
	}
	for _, key := range []string{"node_id", "candidate_revision", "candidate_hash", "opened_game_ms", "deadline_game_ms", "automatic"} {
		if _, exists := proofObject[key]; !exists {
			return store.Invalid
		}
	}
	candidates := service.Content.Candidates(allocation.Manifest.Seed, fmt.Sprintf("growth_%d", len(stats.Choices)+1), stats.LoadoutID, stats.Choices)
	if proof.Node != fmt.Sprintf("growth_%d", len(stats.Choices)+1) || proof.Revision < 1 || proof.Revision > 3 || proof.Opened < 0 || proof.Opened > frame.Elapsed || proof.Deadline != proof.Opened+integer(service.Content.Section("p4"), "choice_timeout_game_ms") || len(candidates) != 3 || proof.Hash != store.Hash([]byte(strings.Join(candidates, "|"))) || !slices.Contains(candidates, fact.Target) || slices.Contains(stats.Choices, fact.Target) {
		return store.Conflict
	}
	if proof.Automatic {
		if frame.Elapsed < proof.Deadline || fact.Target != candidates[0] {
			return store.Conflict
		}
	} else if frame.Elapsed >= proof.Deadline {
		return store.Conflict
	}
	stats.Choices = append(stats.Choices, fact.Target)
	allocation.Stats[*fact.Actor] = stats
	return nil
}
