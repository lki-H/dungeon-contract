package gameplay

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"strconv"
	"strings"
	"time"

	"game-realtime-gm/backend/internal/pve/run"
	"game-realtime-gm/backend/internal/pve/store"
	"game-realtime-gm/backend/internal/pve/task"
)

type Stats struct {
	PlayerID           string   `json:"player_id"`
	Slot               int      `json:"slot"`
	LoadoutID          string   `json:"loadout_id"`
	Damage             int64    `json:"damage"`
	Kills              int64    `json:"kills"`
	Assists            int64    `json:"assists"`
	Interactions       int64    `json:"interactions"`
	Deaths             int      `json:"deaths"`
	Knockdowns         int      `json:"knockdowns,omitempty"`
	Rescues            int      `json:"rescues,omitempty"`
	ProtectedDamage    int64    `json:"protected_damage,omitempty"`
	ControlEffect      int64    `json:"control_effect_ms,omitempty"`
	ClearedProjectiles int64    `json:"cleared_projectiles,omitempty"`
	Choices            []string `json:"choices"`
	Effective          bool     `json:"effective_contribution"`
}
type Fact struct {
	SpawnOrigin      string          `json:"spawn_origin,omitempty"`
	LifeGeneration   int64           `json:"life_generation,omitempty"`
	DownedGeneration int64           `json:"downed_generation,omitempty"`
	EventGameMS      int64           `json:"event_game_ms,omitempty"`
	Proof            json.RawMessage `json:"proof,omitempty"`
	EventID          string          `json:"event_id"`
	Kind             string          `json:"kind"`
	Actor            *string         `json:"actor_player_id"`
	Target           string          `json:"target_id"`
	Contributors     []string        `json:"contributors"`
	Action           *string         `json:"action_id"`
	Amount           int64           `json:"effective_amount"`
	Connected        *bool           `json:"connected,omitempty"`
	Connection       int64           `json:"connection_generation,omitempty"`
	Phase            string          `json:"phase,omitempty"`
	Reinforcement    string          `json:"reinforcement_action_id,omitempty"`
	Eligible         *bool           `json:"contract_kill_eligible,omitempty"`
}
type Terminal struct {
	Outcome  string `json:"outcome"`
	Reason   string `json:"reason"`
	Frame    int64  `json:"cutoff_frame"`
	Sequence int64  `json:"cutoff_sequence"`
}
type Summary struct {
	Participants []Stats `json:"participants"`
	Eligible     bool    `json:"trial_eligible"`
	Reason       string  `json:"ineligible_reason,omitempty"`
}
type Frame struct {
	WallElapsed int64     `json:"wall_elapsed_ms,omitempty"`
	ManualPause int64     `json:"manual_pause_ms,omitempty"`
	Schema      int       `json:"schema_version"`
	Source      string    `json:"source"`
	RunID       string    `json:"run_id"`
	Instance    string    `json:"instance_id"`
	Boot        string    `json:"boot_id"`
	Assignment  string    `json:"assignment_id"`
	Generation  int64     `json:"source_generation"`
	Sequence    int64     `json:"sequence"`
	ID          string    `json:"frame_id"`
	WorldFrame  int64     `json:"world_frame_id"`
	Elapsed     int64     `json:"elapsed_ms"`
	Control     int64     `json:"control_version"`
	Facts       []Fact    `json:"facts"`
	Terminal    *Terminal `json:"terminal"`
	Summary     *Summary  `json:"summary"`
	Fingerprint string    `json:"fingerprint"`
}
type Receipt struct {
	Status         string                      `json:"status"`
	Applied        int64                       `json:"applied_sequence"`
	Control        int64                       `json:"control_version"`
	RunStatus      string                      `json:"run_status"`
	Reinforcements map[string]run.Spawn        `json:"reinforcements"`
	Requirements   map[string]map[string]int64 `json:"task_requirements"`
}

func (service *Service) ParseFrame(data []byte) (Frame, error) {
	if service.Content.V3 != nil {
		return validateV3Frame(data, service.Content)
	}
	var frame Frame
	if len(data) > int(service.Content.Millis("max_frame_bytes")) {
		return frame, store.Invalid
	}
	value, err := DecodeJSON(data)
	if err != nil {
		return frame, store.Invalid
	}
	if err := ValidateSchema(value, service.Content.FrameSchema); err != nil {
		return frame, store.Invalid
	}
	object := value.(map[string]any)
	fingerprint := object["fingerprint"]
	delete(object, "fingerprint")
	if fingerprint != store.Hash(Canonical(object)) {
		return frame, store.Conflict
	}
	if err := json.Unmarshal(data, &frame); err != nil {
		return frame, store.Invalid
	}
	if len(frame.Facts) > int(service.Content.Millis("max_frame_facts")) {
		return frame, store.Invalid
	}
	return frame, nil
}
func (service *Service) Frame(ctx context.Context, data []byte) (*Receipt, error) {
	frame, err := service.ParseFrame(data)
	if err != nil {
		return nil, err
	}
	var result *Receipt
	err = store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		state, allocation, err := service.load(ctx, transaction, frame.RunID)
		if err != nil {
			return err
		}
		if frame.Instance != service.InstanceID || frame.Assignment != allocation.Manifest.AssignmentID || frame.Boot != allocation.Manifest.BootID || frame.Generation != allocation.Manifest.Generation {
			return store.Forbidden
		}
		var priorHash string
		var priorReceipt []byte
		err = transaction.QueryRowContext(ctx, "SELECT fingerprint,receipt FROM pve_dc_frame_receipts WHERE frame_uuid=? AND run_id=?", frame.ID, state.ID).Scan(&priorHash, &priorReceipt)
		if err == nil {
			if priorHash != frame.Fingerprint {
				return store.Conflict
			}
			var receipt Receipt
			if err := json.Unmarshal(priorReceipt, &receipt); err != nil {
				return err
			}
			result = &receipt
			return nil
		}
		if !errors.Is(err, sql.ErrNoRows) {
			return err
		}
		if state.Status != "running" {
			return Expired
		}
		for _, spawn := range state.Spawns {
			if spawn.Status == "pending_spawn" && !(service.S1() && allocation.Clock != nil && allocation.Clock.State == "paused") && !service.Now().Before(spawn.Deadline) {
				return Expired
			}
		}
		if err := service.bound(ctx, transaction, allocation); err != nil {
			return err
		}
		if frame.Control != allocation.Control {
			return Fence
		}
		if service.S1() {
			if err := service.validateS1FrameClock(allocation, frame); err != nil {
				return err
			}
		}
		if frame.Sequence <= state.AppliedSequence || frame.Sequence > state.AppliedSequence+service.Content.Millis("reorder_window") || frame.Sequence > service.Content.Millis("max_key_frames") {
			return store.Conflict
		}
		if allocation.StartedAt == nil || frame.Elapsed > service.Now().Sub(*allocation.StartedAt).Milliseconds()+service.Content.Millis("input_timeout_ms") || service.Now().After(state.DeadlineAt.Add(time.Duration(integer(service.Content.Section("operation"), "terminal_grace_ms"))*time.Millisecond)) {
			return Expired
		}
		var pending, bytes int64
		if err := transaction.QueryRowContext(ctx, "SELECT COUNT(*),COALESCE(SUM(OCTET_LENGTH(payload)),0) FROM pve_dc_frame_receipts WHERE run_id=? AND status='received'", state.ID).Scan(&pending, &bytes); err != nil {
			return err
		}
		if pending >= service.Content.Millis("max_pending_frames") || bytes+int64(len(data)) > service.Content.Millis("max_pending_bytes") {
			return store.Conflict
		}
		receipt := Receipt{Status: "received", Applied: state.AppliedSequence, Control: allocation.Control, RunStatus: state.Status}
		if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_frame_receipts(frame_uuid,run_id,source_generation,sequence_no,world_frame_id,fingerprint,payload,receipt,status,received_at) VALUES(?,?,?,?,?,?,?,?,'received',?)", frame.ID, state.ID, frame.Generation, frame.Sequence, frame.WorldFrame, frame.Fingerprint, data, store.JSON(receipt), service.Now().UTC()); err != nil {
			return err
		}
		for state.Status == "running" {
			var nextData []byte
			err := transaction.QueryRowContext(ctx, "SELECT payload FROM pve_dc_frame_receipts WHERE run_id=? AND source_generation=? AND sequence_no=? AND status='received'", state.ID, frame.Generation, state.AppliedSequence+1).Scan(&nextData)
			if errors.Is(err, sql.ErrNoRows) {
				break
			}
			if err != nil {
				return err
			}
			var next Frame
			if err := json.Unmarshal(nextData, &next); err != nil {
				return err
			}
			if next.Control != allocation.Control {
				return Fence
			}
			if next.WorldFrame <= allocation.LastWorldFrame || next.Elapsed < allocation.Elapsed || allocation.Facts+len(next.Facts) > int(service.Content.Millis("max_facts")) {
				return store.Conflict
			}
			if err := service.applyFrame(ctx, transaction, state, allocation, next); err != nil {
				return err
			}
			allocation.LastWorldFrame = next.WorldFrame
			allocation.Elapsed = next.Elapsed
			allocation.Facts += len(next.Facts)
			ack := service.receipt(state, allocation)
			if _, err := transaction.ExecContext(ctx, "UPDATE pve_dc_frame_receipts SET status='applied',receipt=?,applied_at=? WHERE frame_uuid=?", store.JSON(ack), service.Now().UTC(), next.ID); err != nil {
				return err
			}
		}
		var buffered int
		if err := transaction.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_dc_frame_receipts WHERE run_id=? AND status='received'", state.ID).Scan(&buffered); err != nil {
			return err
		}
		if buffered > 0 && allocation.GapAt == nil {
			now := service.Now().UTC()
			allocation.GapAt = &now
		} else if buffered == 0 {
			allocation.GapAt = nil
		}
		var receiptData []byte
		if err := transaction.QueryRowContext(ctx, "SELECT receipt FROM pve_dc_frame_receipts WHERE frame_uuid=?", frame.ID).Scan(&receiptData); err != nil {
			return err
		}
		if err := json.Unmarshal(receiptData, &result); err != nil {
			return err
		}
		return service.save(ctx, transaction, state, allocation)
	})
	return result, err
}
func (service *Service) receipt(state *run.State, allocation *Allocation) *Receipt {
	requirements := map[string]map[string]int64{}
	for _, member := range state.Participants {
		definition, exists := state.Rules.Task(member.TaskKey)
		if !exists || member.Compatibility != "compatible" || member.Status == "left" {
			continue
		}
		counts := map[string]int64{}
		active := map[string]bool{}
		for _, objective := range definition.Objectives {
			active[objective.Key] = true
			for _, dependency := range objective.DependsOn {
				active[objective.Key] = active[objective.Key] && member.TaskProgress[dependency] >= requiredCount(definition.Objectives, dependency)
			}
		}
		for _, objective := range definition.Objectives {
			if active[objective.Key] && !member.TaskCompleted && member.TaskProgress[objective.Key] < objective.Required {
				counts[objective.Type+":"+objective.TargetID] = objective.Required - member.TaskProgress[objective.Key]
			}
		}
		requirements[strconv.FormatInt(member.PlayerID, 10)] = counts
	}
	return &Receipt{Status: "applied", Applied: state.AppliedSequence, Control: allocation.Control, RunStatus: state.Status, Reinforcements: state.Spawns, Requirements: requirements}
}
func requiredCount(values []task.Objective, id string) int64 {
	for _, value := range values {
		if value.Key == id {
			return value.Required
		}
	}
	return 0
}
func (service *Service) applyFrame(ctx context.Context, transaction *sql.Tx, state *run.State, allocation *Allocation, frame Frame) error {
	rows, err := transaction.QueryContext(ctx, "SELECT command_type,player_id FROM pve_dc_control_commands WHERE run_id=? AND control_version>? AND control_version<=? ORDER BY control_version", state.ID, allocation.AppliedControl, frame.Control)
	if err != nil {
		return err
	}
	for rows.Next() {
		var kind string
		var player sql.NullInt64
		if err := rows.Scan(&kind, &player); err != nil {
			rows.Close()
			return err
		}
		if kind == "pause" || kind == "resume" {
			continue
		}
		member := state.Member(&player.Int64)
		if member == nil {
			rows.Close()
			return store.Forbidden
		}
		member.Status = "left"
		member.LeftSequence = frame.Sequence
		allocation.TrialEligible = false
	}
	err = rows.Err()
	rows.Close()
	if err != nil {
		return err
	}
	if _, err := transaction.ExecContext(ctx, "UPDATE pve_dc_control_commands SET status='applied',effective_frame=? WHERE run_id=? AND control_version<=? AND status='issued' AND command_type NOT IN ('pause','resume')", frame.WorldFrame, state.ID, frame.Control); err != nil {
		return err
	}
	allocation.AppliedControl = frame.Control
	if service.S1() {
		var pendingClock sql.NullInt64
		if err := transaction.QueryRowContext(ctx, "SELECT MIN(control_version) FROM pve_dc_control_commands WHERE run_id=? AND control_version<=? AND command_type IN ('pause','resume') AND status='issued'", state.ID, frame.Control).Scan(&pendingClock); err != nil {
			return err
		}
		if pendingClock.Valid {
			allocation.AppliedControl = pendingClock.Int64 - 1
		}
	}
	events := []run.Event{}
	seen := map[string]bool{}
	priorFacts := map[string]bool{}
	for key, value := range state.Facts {
		priorFacts[key] = value
	}
	for index, fact := range frame.Facts {
		if seen[fact.EventID] {
			return store.Invalid
		}
		seen[fact.EventID] = true
		var priorEvent int
		if err := transaction.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_run_events WHERE event_id=?", fact.EventID).Scan(&priorEvent); err != nil {
			return err
		}
		if priorEvent > 0 {
			return store.Conflict
		}
		var eventPlayer any
		if fact.Actor != nil {
			player, err := strconv.ParseInt(*fact.Actor, 10, 64)
			if err != nil {
				return store.Invalid
			}
			eventPlayer = player
		}
		if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_run_events(event_id,run_id,source,source_generation,sequence_no,event_type,actor_player_id,contributors,target_id,payload,status,fingerprint,occurred_at) VALUES(?,?,'unity_game_server',?,?,?,?,?,?,?,'applied',?,?)", fact.EventID, state.ID, frame.Generation, frame.Sequence*128+int64(index), fact.Kind, eventPlayer, store.JSON(fact.Contributors), fact.Target, store.JSON(fact), store.Hash(store.JSON(fact)), service.Now().UTC()); err != nil {
			return err
		}
		if fact.Kind == "phase" {
			if fact.Actor != nil {
				return store.Invalid
			}
			if fact.Phase == "technical_wait" {
				allocation.TrialEligible = false
			}
			continue
		}
		if fact.Actor == nil {
			return store.Invalid
		}
		player, err := strconv.ParseInt(*fact.Actor, 10, 64)
		if err != nil {
			return store.Invalid
		}
		member := state.Member(&player)
		if member == nil || member.Status != "participating" {
			return store.Forbidden
		}
		if fact.Kind == "growth_choice" {
			if fact.LifeGeneration != member.LifeGeneration && member.LifeGeneration > 0 {
				return store.Conflict
			}
			if err := service.applyP4Choice(allocation, fact, frame); err != nil {
				return err
			}
			continue
		}
		if fact.Kind == "presence" {
			if fact.Connected == nil || fact.Connection != allocation.Connections[*fact.Actor] {
				return store.Forbidden
			}
			member.Connected = *fact.Connected
			if member.Connected {
				member.DisconnectedAt = nil
			} else if member.DisconnectedAt == nil {
				now := service.Now().UTC()
				member.DisconnectedAt = &now
			}
			continue
		}
		contributors := []int64{}
		for _, id := range fact.Contributors {
			contributor, err := strconv.ParseInt(id, 10, 64)
			if err != nil || state.Member(&contributor) == nil {
				return store.Invalid
			}
			contributors = append(contributors, contributor)
		}
		kind := fact.Kind
		target := fact.Target
		if kind == "reinforcement_spawned" || kind == "reinforcement_failed" {
			kind = "reinforcement." + strings.TrimPrefix(kind, "reinforcement_")
			target = fact.Reinforcement
			if target == "" {
				return store.Invalid
			}
		}
		payload := json.RawMessage(`{}`)
		if service.P3() {
			payload, err = service.p3EventPayload(state, fact, frame)
			if err != nil {
				return err
			}
		}
		if kind == "damage" {
			if fact.Action == nil || fact.Amount <= 0 {
				return store.Invalid
			}
			if !service.P3() {
				payload = store.JSON(map[string]any{"action_id": *fact.Action, "effective_amount": fact.Amount})
			}
		}
		if kind == "kill" && fact.Eligible == nil {
			return store.Invalid
		}
		event := run.Event{EventID: fact.EventID, RunID: state.ID, Source: "unity_game_server", SourceGeneration: frame.Generation, Sequence: frame.Sequence, EventType: kind, ActorPlayerID: &player, Contributors: contributors, TargetID: target, Payload: payload, OccurredAt: service.Now().UTC(), ContractEligible: fact.Eligible}
		events = append(events, event)
	}
	apply := run.ApplyFrame
	if service.P3() {
		apply = run.ApplyP3Frame
	}
	if err := apply(ctx, transaction, state, events, frame.Sequence, service.Now().UTC()); err != nil {
		return err
	}
	participating := false
	for _, member := range state.Participants {
		participating = participating || member.Status == "participating"
	}
	if !participating {
		allocation.TrialEligible = false
		run.EndAuthority(state, "team_abandoned", service.Now().UTC())
		allocation.Status = "draining"
		if service.P3() {
			if frame.Terminal == nil || frame.Summary == nil || frame.Terminal.Reason != "team_abandoned" || frame.Terminal.Outcome != "aborted" || frame.Terminal.Frame != frame.WorldFrame || frame.Terminal.Sequence != frame.Sequence {
				return store.Invalid
			}
			for _, member := range state.Participants {
				id := strconv.FormatInt(member.PlayerID, 10)
				stats := allocation.Stats[id]
				stats.Deaths = member.Deaths
				stats.Knockdowns = member.Knockdowns
				stats.Rescues = member.Rescues
				stats.ProtectedDamage = member.ProtectedDamage
				stats.ControlEffect = member.ControlEffect
				stats.ClearedProjectiles = member.ClearedProjectiles
				stats.Effective = member.Contribution > 0
				allocation.Stats[id] = stats
			}
			if err := service.summary(ctx, transaction, state, allocation, frame); err != nil {
				return err
			}
		}
		return run.QueueSettlement(ctx, transaction, state)
	}
	if frame.Terminal != nil && state.Status == "running" {
		if service.P3() && frame.Terminal.Outcome == "aborted" && frame.Terminal.Reason == "technical_failure" {
			run.EndAuthority(state, "technical_failure", service.Now().UTC())
		} else if frame.Terminal.Outcome == "failed" && frame.Terminal.Reason == "timeout" && frame.Elapsed >= integer(service.Content.Section("operation"), "hard_deadline_ms") {
			run.EndAuthority(state, "timeout", service.Now().UTC())
		} else if frame.Terminal.Outcome == "failed" && frame.Terminal.Reason == "team_wipe" {
			alive, pending := false, false
			for _, member := range state.Participants {
				alive = alive || (member.Status == "participating" && member.LifeStatus == "alive")
			}
			for _, spawn := range state.Spawns {
				pending = pending || spawn.Status == "pending_spawn"
			}
			if alive || pending || state.ReinforcementUsed < state.ReinforcementBudget {
				return store.Conflict
			}
			run.EndAuthority(state, "team_wipe", service.Now().UTC())
		} else {
			return store.Conflict
		}
	}
	for _, fact := range frame.Facts {
		if fact.Actor == nil {
			continue
		}
		stats := allocation.Stats[*fact.Actor]
		key := fact.Kind + ":" + fact.Target
		if fact.Kind == "damage" && fact.Action != nil {
			key = "damage:" + *fact.Action
		}
		if fact.Kind == "interact" || fact.Kind == "reach" {
			key += ":" + *fact.Actor
		}
		if priorFacts[key] {
			continue
		}
		priorFacts[key] = true
		switch fact.Kind {
		case "damage":
			stats.Damage += fact.Amount
		case "kill":
			stats.Kills++
		case "interact":
			stats.Interactions++
		}
		allocation.Stats[*fact.Actor] = stats
	}
	for _, member := range state.Participants {
		stats := allocation.Stats[strconv.FormatInt(member.PlayerID, 10)]
		stats.Deaths = member.Deaths
		stats.Knockdowns = member.Knockdowns
		stats.Rescues = member.Rescues
		stats.ProtectedDamage = member.ProtectedDamage
		stats.ControlEffect = member.ControlEffect
		stats.ClearedProjectiles = member.ClearedProjectiles
		stats.Effective = member.Contribution > 0
		allocation.Stats[stats.PlayerID] = stats
	}
	if state.Status == "ending" {
		if frame.Terminal == nil || frame.Summary == nil {
			return store.Invalid
		}
		outcome, reason := "success", "objectives_complete"
		if state.EndReason != "success" {
			outcome = "failed"
			reason = state.EndReason
		}
		if service.P3() && (reason == "technical_failure" || reason == "team_abandoned") {
			outcome = "aborted"
		}
		if frame.Terminal.Outcome != outcome || frame.Terminal.Reason != reason || frame.Terminal.Frame != frame.WorldFrame || frame.Terminal.Sequence != frame.Sequence {
			return store.Conflict
		}
		if err := service.summary(ctx, transaction, state, allocation, frame); err != nil {
			return err
		}
		allocation.Status = "draining"
		if err := run.QueueSettlement(ctx, transaction, state); err != nil {
			return err
		}
	} else if frame.Terminal != nil {
		return store.Conflict
	}
	return nil
}
func (service *Service) summary(ctx context.Context, transaction *sql.Tx, state *run.State, allocation *Allocation, frame Frame) error {
	if len(frame.Summary.Participants) != len(state.Participants) {
		return store.Invalid
	}
	seen := map[string]bool{}
	for _, stats := range frame.Summary.Participants {
		prior, exists := allocation.Stats[stats.PlayerID]
		if !exists || seen[stats.PlayerID] || stats.Slot != prior.Slot || stats.LoadoutID != prior.LoadoutID || stats.Deaths != prior.Deaths || stats.Effective != prior.Effective || stats.Damage < prior.Damage || stats.Kills < prior.Kills || stats.Assists < prior.Assists || stats.Interactions < prior.Interactions {
			return store.Conflict
		}
		if service.P3() && (stats.Knockdowns != prior.Knockdowns || stats.Rescues != prior.Rescues || stats.ProtectedDamage < prior.ProtectedDamage || stats.ControlEffect < prior.ControlEffect || stats.ClearedProjectiles < prior.ClearedProjectiles || (!service.P4() && len(stats.Choices) != 0) || service.P4() && strings.Join(stats.Choices, "|") != strings.Join(prior.Choices, "|")) {
			return store.Conflict
		}
		seen[stats.PlayerID] = true
		for _, choice := range stats.Choices {
			legal := false
			for _, upgrade := range objects(service.Content.Document, "upgrades") {
				legal = legal || (text(upgrade, "id") == choice && (text(upgrade, "scope") == "common" || text(upgrade, "scope") == stats.LoadoutID))
			}
			if !legal {
				return store.Invalid
			}
		}
		allocation.Stats[stats.PlayerID] = stats
		player, _ := strconv.ParseInt(stats.PlayerID, 10, 64)
		if service.P3() {
			member := state.Member(&player)
			member.ProtectedDamage = stats.ProtectedDamage
			member.ControlEffect = stats.ControlEffect
			member.ClearedProjectiles = stats.ClearedProjectiles
		}
		if !stats.Effective || state.Member(&player).Status == "left" {
			allocation.TrialEligible = false
		}
		if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_participant_stats(run_id,player_id,damage,kills,assists,interactions,deaths,last_sequence,loadout,choices,effective_contribution,final_hash) VALUES(?,?,?,?,?,?,?,?,?,?,?,?) ON DUPLICATE KEY UPDATE damage=VALUES(damage),kills=VALUES(kills),assists=VALUES(assists),interactions=VALUES(interactions),deaths=VALUES(deaths),last_sequence=VALUES(last_sequence),choices=VALUES(choices),effective_contribution=VALUES(effective_contribution),final_hash=VALUES(final_hash)", state.ID, player, stats.Damage, stats.Kills, stats.Assists, stats.Interactions, stats.Deaths, frame.Sequence, stats.LoadoutID, store.JSON(stats.Choices), stats.Effective, store.Hash(store.JSON(stats))); err != nil {
			return err
		}
		if service.P3() {
			if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_v4_life_stats(run_id,player_id,slot,knockdowns,rescues,protected_damage,control_effect_ms) VALUES(?,?,?,?,?,?,?)", state.ID, player, stats.Slot, stats.Knockdowns, stats.Rescues, stats.ProtectedDamage, stats.ControlEffect); err != nil {
				return err
			}
		}
	}
	allocation.TrialEligible = allocation.TrialEligible && frame.Summary.Eligible
	if allocation.Engineering {
		allocation.TrialEligible = false
	}
	if service.P3() {
		allocation.P3FinalSummary = true
	}
	if allocation.Manifest.Mode == "trial" {
		reason := frame.Summary.Reason
		if allocation.Engineering {
			reason = "debug"
		}
		record := store.ID("record")
		if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_trial_records(record_id,run_id,trial_id,trial_version,initial_player_count,elapsed_ms,completed_at,eligible,status,summary_hash,reason) VALUES(?,?,'dc_ruins_trial',?,?,?,?,?,'pending',?,?)", record, state.ID, allocation.Manifest.TrialVersion, len(state.Participants), frame.Elapsed, service.Now().UTC(), allocation.TrialEligible, store.Hash(store.JSON(frame.Summary)), reason); err != nil {
			return err
		}
		for _, member := range allocation.Manifest.Members {
			player, _ := strconv.ParseInt(member.PlayerID, 10, 64)
			if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_trial_record_members(record_id,player_id,slot,loadout_id) VALUES(?,?,?,?)", record, player, member.Slot, member.LoadoutID); err != nil {
				return err
			}
		}
	}
	return nil
}
