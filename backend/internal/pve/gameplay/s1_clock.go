package gameplay

import (
	"context"
	"database/sql"
	"encoding/json"
	"strconv"
	"time"

	"game-realtime-gm/backend/internal/pve/run"
	"game-realtime-gm/backend/internal/pve/store"
)

type S1Clock struct {
	State       string     `json:"state"`
	Revision    int64      `json:"revision"`
	ManualPause int64      `json:"manual_pause_ms"`
	PauseID     string     `json:"pause_id"`
	PauseAt     *time.Time `json:"pause_at,omitempty"`
}

type S1Ack struct {
	Protocol   int    `json:"protocol_version"`
	Assignment string `json:"assignment_id"`
	Boot       string `json:"boot_id"`
	Generation int64  `json:"source_generation"`
	Operation  string `json:"operation_id"`
	Control    int64  `json:"control_version"`
	Action     string `json:"action"`
	Frame      int64  `json:"effective_frame"`
	Game       int64  `json:"game_elapsed_ms"`
	Wall       int64  `json:"wall_elapsed_ms"`
	Pause      int64  `json:"manual_pause_ms"`
	PauseID    string `json:"pause_id"`
	Revision   int64  `json:"clock_revision"`
	Checkpoint string `json:"checkpoint_hash"`
}

func (service *Service) S1() bool {
	return (service.Content.Rules.Version == "dc_ruins.v4.s1" || service.Content.Rules.Version == "dc_ruins.v4.s2" || service.Content.Rules.Version == "dc_ruins.v4.p2.1" || service.Content.Rules.Version == "dc_ruins.v4.p2.multiplayer" || service.P3()) && (service.Content.ProtocolVersion() == 4 || service.V5())
}

func (service *Service) StagePlayerLimit() int {
	if service.Content.Rules.Version == "dc_ruins.v4.p2.multiplayer" || service.P3() {
		return 4
	}
	if service.Content.Rules.Version == "dc_ruins.v3.p1" || service.S1() {
		return 1
	}
	return 4
}

func (service *Service) ClockCommandTx(ctx context.Context, transaction *sql.Tx, player int64, operation, kind string, data json.RawMessage) (map[string]any, error) {
	if !service.S1() {
		return nil, store.Invalid
	}
	value, err := DecodeJSON(data)
	if err != nil {
		return nil, store.Invalid
	}
	input, ok := value.(map[string]any)
	if !ok || len(input) != 4 || input["run_id"] == nil || input["reason"] == nil || input["expected_clock_revision"] == nil {
		return nil, store.Invalid
	}
	id := text(input, "run_id")
	for _, key := range []string{"run_id", "reason", "expected_clock_revision", "pause_id"} {
		if _, exists := input[key]; !exists {
			return nil, store.Invalid
		}
	}
	state, allocation, err := service.load(ctx, transaction, id)
	if err != nil {
		return nil, err
	}
	member := state.Member(&player)
	if member == nil || member.Status != "participating" || state.Status != "running" || allocation.Manifest.Mode != "normal" || len(state.Participants) != 1 {
		return nil, store.Forbidden
	}
	if allocation.Clock == nil || allocation.Clock.Revision != integer(input, "expected_clock_revision") {
		return nil, store.Conflict
	}
	var pending int
	if err := transaction.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_dc_control_commands WHERE run_id=? AND status='issued'", id).Scan(&pending); err != nil {
		return nil, err
	}
	if pending != 0 || allocation.AppliedControl != allocation.Control {
		return nil, Fence
	}
	if service.P3() {
		var clockOperations int
		if err := transaction.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_dc_control_commands WHERE run_id=? AND command_type IN ('pause','resume')", id).Scan(&clockOperations); err != nil {
			return nil, err
		}
		if clockOperations >= 256 {
			return nil, store.Conflict
		}
	}
	action, pauseID := "pause", store.ID("pause")
	if kind == "v2.run.pause" {
		if allocation.Clock.State != "running" || input["pause_id"] != nil || (text(input, "reason") != "menu" && text(input, "reason") != "focus_loss") {
			return nil, store.Invalid
		}
	} else if kind == "v2.run.resume" {
		action, pauseID = "resume", text(input, "pause_id")
		if allocation.Clock.State != "paused" || pauseID != allocation.Clock.PauseID || text(input, "reason") != "explicit_resume" {
			return nil, store.Invalid
		}
	} else {
		return nil, store.Invalid
	}
	allocation.Control++
	payload := map[string]any{"pause_id": pauseID, "clock_revision": allocation.Clock.Revision, "player_id": strconv.FormatInt(player, 10), "reason": text(input, "reason")}
	if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_control_commands(run_id,control_version,operation_id,command_type,player_id,payload,status) VALUES(?,?,?,?,?,?,'issued')", id, allocation.Control, operation, action, player, store.JSON(payload)); err != nil {
		return nil, err
	}
	if err := service.save(ctx, transaction, state, allocation); err != nil {
		return nil, err
	}
	return map[string]any{"run_id": id, "operation_id": operation, "status": "pending", "control_version": allocation.Control, "clock_revision": allocation.Clock.Revision, "pause_id": pauseID}, nil
}

func (service *Service) ClockAck(ctx context.Context, id string, input S1Ack) (map[string]any, error) {
	if !service.S1() || input.Protocol != service.Content.ProtocolVersion() || input.Game < 0 || input.Wall < 0 || input.Pause < 0 || input.Game+input.Pause != input.Wall || input.Frame < 0 || len(input.Checkpoint) != 64 {
		return nil, store.Invalid
	}
	var result map[string]any
	err := store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		state, allocation, err := service.load(ctx, transaction, id)
		if err != nil {
			return err
		}
		if input.Assignment != allocation.Manifest.AssignmentID || input.Boot != allocation.Manifest.BootID || input.Generation != allocation.Manifest.Generation {
			return store.Forbidden
		}
		var kind, status string
		var payloadBytes []byte
		if err := transaction.QueryRowContext(ctx, "SELECT command_type,status,payload FROM pve_dc_control_commands WHERE run_id=? AND operation_id=? AND control_version=?", id, input.Operation, input.Control).Scan(&kind, &status, &payloadBytes); err != nil {
			return err
		}
		var payload map[string]any
		if err := json.Unmarshal(payloadBytes, &payload); err != nil {
			return err
		}
		if status == "applied" {
			var hash string
			var receipt []byte
			if err := transaction.QueryRowContext(ctx, "SELECT request_hash,receipt FROM pve_dc_v4_clock_receipts WHERE run_id=? AND operation_id=?", id, input.Operation).Scan(&hash, &receipt); err != nil {
				return err
			}
			if hash != store.Hash(store.JSON(input)) {
				return store.Conflict
			}
			return json.Unmarshal(receipt, &result)
		}
		if state.Status != "running" || allocation.Clock == nil || kind != input.Action || (kind != "pause" && kind != "resume") || input.Control > allocation.Control || input.Control != allocation.AppliedControl+1 || input.PauseID != payload["pause_id"] || input.Revision != allocation.Clock.Revision+1 || input.Frame < allocation.LastWorldFrame {
			return store.Conflict
		}
		if err := service.bound(ctx, transaction, allocation); err != nil {
			return err
		}
		now := service.Now().UTC()
		wall := now.Sub(*allocation.StartedAt).Milliseconds()
		if input.Wall > wall+250 || wall-input.Wall > 2000 || input.Game < allocation.Elapsed || input.Game > 1800000 || input.Wall > 3600000 {
			return store.Invalid
		}
		if kind == "pause" {
			if allocation.Clock.State != "running" || input.Pause != allocation.Clock.ManualPause {
				return store.Conflict
			}
			pauseAt := allocation.StartedAt.Add(time.Duration(input.Wall) * time.Millisecond)
			allocation.Clock.State, allocation.Clock.PauseID, allocation.Clock.PauseAt = "paused", input.PauseID, &pauseAt
			if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_v4_pause_intervals(run_id,pause_id,begin_operation,begin_wall_ms,begin_game_ms,status) VALUES(?,?,?,?,?,'open')", id, input.PauseID, input.Operation, input.Wall, input.Game); err != nil {
				return err
			}
		} else {
			if allocation.Clock.State != "paused" || allocation.Clock.PauseAt == nil {
				return store.Conflict
			}
			beginWall := allocation.Clock.PauseAt.Sub(*allocation.StartedAt).Milliseconds()
			expected := allocation.Clock.ManualPause + input.Wall - beginWall
			if input.Pause != expected {
				return store.Conflict
			}
			for id, spawn := range state.Spawns {
				if spawn.Status == "pending_spawn" {
					spawn.Deadline = spawn.Deadline.Add(time.Duration(input.Pause-allocation.Clock.ManualPause) * time.Millisecond)
					state.Spawns[id] = spawn
				}
			}
			allocation.Clock.ManualPause = input.Pause
			allocation.Clock.State = "running"
			allocation.Clock.PauseID = ""
			allocation.Clock.PauseAt = nil
			state.DeadlineAt = allocation.StartedAt.Add(time.Duration(1800000+input.Pause) * time.Millisecond)
			if _, err := transaction.ExecContext(ctx, "UPDATE pve_dc_v4_pause_intervals SET end_operation=?,end_wall_ms=?,status='closed' WHERE run_id=? AND pause_id=? AND status='open'", input.Operation, input.Wall, id, input.PauseID); err != nil {
				return err
			}
		}
		allocation.Clock.Revision = input.Revision
		allocation.Elapsed = input.Game
		allocation.AppliedControl = input.Control
		if _, err := transaction.ExecContext(ctx, "UPDATE pve_dc_control_commands SET status='applied',effective_frame=? WHERE run_id=? AND operation_id=?", input.Frame, id, input.Operation); err != nil {
			return err
		}
		if err := service.save(ctx, transaction, state, allocation); err != nil {
			return err
		}
		result = map[string]any{"status": "applied", "run_id": id, "clock_state": allocation.Clock.State, "clock_revision": allocation.Clock.Revision, "manual_pause_ms": input.Pause, "control_version": input.Control, "game_elapsed_ms": input.Game, "wall_elapsed_ms": input.Wall, "pause_id": allocation.Clock.PauseID}
		_, err = transaction.ExecContext(ctx, "INSERT INTO pve_dc_v4_clock_receipts(run_id,operation_id,request_hash,receipt) VALUES(?,?,?,?)", id, input.Operation, store.Hash(store.JSON(input)), store.JSON(result))
		return err
	})
	return result, err
}

func (service *Service) validateS1FrameClock(allocation *Allocation, frame Frame) error {
	if allocation.Clock == nil || allocation.Clock.State != "running" || allocation.StartedAt == nil || frame.ManualPause != allocation.Clock.ManualPause {
		return store.Conflict
	}
	wall := service.Now().Sub(*allocation.StartedAt).Milliseconds()
	if frame.WallElapsed > wall+250 || wall-frame.WallElapsed > 3000 || frame.Elapsed+frame.ManualPause != frame.WallElapsed {
		return store.Invalid
	}
	return nil
}

type S1Presence struct {
	Assignment string `json:"assignment_id"`
	Boot       string `json:"boot_id"`
	Generation int64  `json:"source_generation"`
	Player     string `json:"player_id"`
	Connection int64  `json:"connection_generation"`
	Connected  bool   `json:"connected"`
}

func (service *Service) PresenceS1(ctx context.Context, id string, input S1Presence) error {
	if !service.S1() {
		return store.Invalid
	}
	return store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		state, allocation, err := service.load(ctx, transaction, id)
		if err != nil {
			return err
		}
		if input.Assignment != allocation.Manifest.AssignmentID || input.Boot != allocation.Manifest.BootID || input.Generation != allocation.Manifest.Generation || input.Connection != allocation.Connections[input.Player] {
			return store.Forbidden
		}
		player, err := strconv.ParseInt(input.Player, 10, 64)
		if err != nil {
			return store.Invalid
		}
		member := state.Member(&player)
		if member == nil || member.Status != "participating" {
			return store.Forbidden
		}
		member.Connected = input.Connected
		if !input.Connected && member.DisconnectedAt == nil {
			at := service.Now().UTC()
			member.DisconnectedAt = &at
		}
		if input.Connected {
			member.DisconnectedAt = nil
		}
		if !input.Connected && allocation.Clock != nil && allocation.Clock.State == "paused" {
			if err := service.forceResumeS1(ctx, transaction, state, allocation, player); err != nil {
				return err
			}
		}
		return service.save(ctx, transaction, state, allocation)
	})
}

func (service *Service) forceResumeS1(ctx context.Context, transaction *sql.Tx, state *run.State, allocation *Allocation, player int64) error {
	var pending int
	if err := transaction.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_dc_control_commands WHERE run_id=? AND command_type='resume' AND status='issued'", state.ID).Scan(&pending); err != nil {
		return err
	}
	if pending > 0 {
		return nil
	}
	allocation.Control++
	payload := map[string]any{"pause_id": allocation.Clock.PauseID, "clock_revision": allocation.Clock.Revision, "player_id": strconv.FormatInt(player, 10), "reason": "explicit_resume"}
	_, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_control_commands(run_id,control_version,operation_id,command_type,player_id,payload,status) VALUES(?,?,?,?,?,?,'issued')", state.ID, allocation.Control, store.ID("disconnect_resume"), "resume", player, store.JSON(payload))
	return err
}

func (service *Service) saveS1Clock(ctx context.Context, transaction *sql.Tx, state *run.State, allocation *Allocation) error {
	if allocation.Clock == nil {
		allocation.Clock = &S1Clock{State: "running", Revision: 1}
	}
	clockNow := service.Now()
	if state.EndedAt != nil {
		clockNow = *state.EndedAt
	}
	wall := clockNow.Sub(*allocation.StartedAt).Milliseconds()
	pause := allocation.Clock.ManualPause
	if allocation.Clock.State == "paused" && allocation.Clock.PauseAt != nil {
		pause += clockNow.Sub(*allocation.Clock.PauseAt).Milliseconds()
	}
	if service.P3() && state.EndedAt != nil {
		wall = allocation.Elapsed + pause
	}
	if wall < pause {
		return store.Invalid
	}
	_, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_v4_run_clocks(run_id,clock_state,clock_revision,game_elapsed_ms,wall_elapsed_ms,manual_pause_ms) VALUES(?,?,?,?,?,?) ON DUPLICATE KEY UPDATE clock_state=VALUES(clock_state),clock_revision=VALUES(clock_revision),game_elapsed_ms=VALUES(game_elapsed_ms),wall_elapsed_ms=VALUES(wall_elapsed_ms),manual_pause_ms=VALUES(manual_pause_ms)", state.ID, allocation.Clock.State, allocation.Clock.Revision, wall-pause, wall, pause)
	return err
}
