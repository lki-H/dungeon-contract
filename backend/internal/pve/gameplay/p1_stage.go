package gameplay

import (
	"context"
	"database/sql"
	"encoding/json"
	"strings"

	"game-realtime-gm/backend/internal/pve/store"
)

type P1Result struct {
	Operation   string `json:"operation_id"`
	Assignment  string `json:"assignment_id"`
	Generation  int64  `json:"source_generation"`
	Control     int64  `json:"control_version"`
	Outcome     string `json:"outcome"`
	Frame       int64  `json:"world_frame_id"`
	Elapsed     int64  `json:"elapsed_ms"`
	Completed   []bool `json:"completed_rooms"`
	SummaryHash string `json:"summary_hash"`
}

func (service *Service) FinishP1(ctx context.Context, id string, input P1Result) (map[string]any, error) {
	if service.P3() {
		return nil, store.Forbidden
	}
	if (service.Content.Rules.Version != "dc_ruins.v3.p1" && !service.S1()) || !uuidPattern.MatchString(input.Operation) || len(input.Completed) != 6 || len(input.SummaryHash) != 64 || strings.Trim(input.SummaryHash, "0123456789abcdef") != "" {
		return nil, store.Invalid
	}
	if input.Outcome != "success" && input.Outcome != "p1_incapacitated" && input.Outcome != "timeout" && input.Outcome != "technical_failure" {
		return nil, store.Invalid
	}
	if input.Outcome == "success" {
		for _, complete := range input.Completed {
			if !complete {
				return nil, store.Conflict
			}
		}
	}
	var result map[string]any
	err := store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		state, allocation, err := service.load(ctx, transaction, id)
		if err != nil {
			return err
		}
		if len(state.Participants) < 1 || len(state.Participants) > service.StagePlayerLimit() || input.Assignment != allocation.Manifest.AssignmentID || input.Generation != allocation.Manifest.Generation {
			return store.Forbidden
		}
		if allocation.P1Result != nil {
			if string(store.JSON(allocation.P1Result)) != string(store.JSON(input)) {
				return store.Conflict
			}
			result = map[string]any{"confirmed": true, "run_id": id, "gameplay_rewards": false}
			return nil
		}
		if err := service.bound(ctx, transaction, allocation); err != nil {
			return err
		}
		if state.Status != "running" || input.Frame < allocation.LastWorldFrame || input.Elapsed < allocation.Elapsed || input.Elapsed > 1800000 || allocation.StartedAt == nil || input.Elapsed > service.Now().Sub(*allocation.StartedAt).Milliseconds()+250 || input.Frame > input.Elapsed*30/1000+4 {
			return store.Conflict
		}
		if input.Control != allocation.Control || allocation.AppliedControl != allocation.Control {
			return Fence
		}
		if service.S1() && (allocation.Clock == nil || allocation.Clock.State != "running" || input.Elapsed > service.Now().Sub(*allocation.StartedAt).Milliseconds()-allocation.Clock.ManualPause+250) {
			return store.Conflict
		}
		allocation.P1Result = &input
		allocation.Elapsed = input.Elapsed
		reason := "p1_demo_" + input.Outcome
		if err := service.end(ctx, transaction, state, allocation, reason); err != nil {
			return err
		}
		result = map[string]any{"confirmed": true, "run_id": id, "gameplay_rewards": false}
		return nil
	})
	return result, err
}

func ValidateP1Geometry(geometry map[string]any) error {
	rooms, ok := geometry["rooms"].([]any)
	if !ok || len(rooms) != 8 {
		return store.Invalid
	}
	for _, raw := range rooms {
		room, ok := raw.(map[string]any)
		if !ok || integer(room, "width") != 18 || integer(room, "height") != 10 {
			return store.Invalid
		}
	}
	expected := []string{"entry", "foyer", "mechanism", "defense", "sanctum", "boss_room", "rune_branch", "supply_branch"}
	for index, raw := range rooms {
		room := raw.(map[string]any)
		if text(room, "id") != expected[index] {
			return store.Invalid
		}
		for _, key := range []string{"spawn", "objective"} {
			point := room[key]
			if point == nil && key == "objective" {
				continue
			}
			values, ok := point.([]any)
			if !ok || len(values) != 2 {
				return store.Invalid
			}
			horizontal, vertical := jsonInteger(values[0]), jsonInteger(values[1])
			if horizontal < 2 || horizontal > 16 || vertical < 2 || vertical > 8 {
				return store.Invalid
			}
		}
		for _, key := range []string{"cover_points", "trap_points"} {
			points, ok := room[key].([]any)
			if !ok || len(points) > 3 {
				return store.Invalid
			}
			for _, rawPoint := range points {
				point, ok := rawPoint.([]any)
				if !ok || len(point) != 2 || jsonInteger(point[0]) < 2 || jsonInteger(point[0]) > 16 || jsonInteger(point[1]) < 2 || jsonInteger(point[1]) > 8 {
					return store.Invalid
				}
			}
		}
		obstacles, ok := room["obstacles_milli"].([]any)
		if !ok || len(obstacles) > 4 {
			return store.Invalid
		}
		for _, rawObstacle := range obstacles {
			rectangle, ok := rawObstacle.([]any)
			if !ok || len(rectangle) != 4 {
				return store.Invalid
			}
			horizontal, vertical, width, height := jsonInteger(rectangle[0]), jsonInteger(rectangle[1]), jsonInteger(rectangle[2]), jsonInteger(rectangle[3])
			if horizontal < 1000 || vertical < 1000 || width < 1 || height < 1 || horizontal+width > 17000 || vertical+height > 9000 {
				return store.Invalid
			}
		}
	}
	doors, ok := geometry["doors"].([]any)
	if !ok || len(doors) != 5 {
		return store.Invalid
	}
	for index, raw := range doors {
		door, ok := raw.(map[string]any)
		if !ok || integer(door, "from") != int64(index) || integer(door, "to") != int64(index+1) || text(door, "side") != "east" {
			return store.Invalid
		}
	}
	_, err := json.Marshal(geometry)
	return err
}

func ValidateP2Geometry(geometry map[string]any) error {
	if text(geometry, "definition_id") != "dc_rooms.v4.p2.1" || text(geometry, "lifecycle_stage") != "p2_room_camera_projectile" {
		return store.Invalid
	}
	rooms, ok := geometry["rooms"].([]any)
	if !ok || len(rooms) != 15 {
		return store.Invalid
	}
	expected := []string{"entry", "foyer", "mechanism", "defense", "sanctum", "boss_room", "rune_branch", "supply_branch"}
	for index, raw := range rooms {
		room, ok := raw.(map[string]any)
		if !ok {
			return store.Invalid
		}
		if text(room, "id") == "" || text(room, "kind") == "" {
			return store.Invalid
		}
		width, height := integer(room, "width"), integer(room, "height")
		if width < 4 || width > 34 || height < 4 || height > 20 {
			return store.Invalid
		}
		if index < len(expected) && text(room, "id") != expected[index] {
			return store.Invalid
		}
		if index < 8 && text(room, "kind") != "room" || index >= 8 && text(room, "kind") != "corridor" {
			return store.Invalid
		}
		if index >= 8 {
			innerWidth, innerHeight := width-2, height-2
			if !((innerWidth >= 4 && innerWidth <= 6 && innerHeight >= 3 && innerHeight <= 4) || (innerHeight >= 4 && innerHeight <= 6 && innerWidth >= 3 && innerWidth <= 4)) || room["objective"] != nil {
				return store.Invalid
			}
		}
		spawn, ok := room["spawn"].([]any)
		if !ok || len(spawn) != 2 || jsonInteger(spawn[0]) < 1 || jsonInteger(spawn[0]) >= width-1 || jsonInteger(spawn[1]) < 1 || jsonInteger(spawn[1]) >= height-1 {
			return store.Invalid
		}
		for _, key := range []string{"cover_points", "trap_points"} {
			points, ok := room[key].([]any)
			if !ok || len(points) > 16 {
				return store.Invalid
			}
			if index >= 8 && len(points) > 0 {
				return store.Invalid
			}
			for _, rawPoint := range points {
				point, ok := rawPoint.([]any)
				if !ok || len(point) != 2 || jsonInteger(point[0]) < 1 || jsonInteger(point[0]) >= width-1 || jsonInteger(point[1]) < 1 || jsonInteger(point[1]) >= height-1 {
					return store.Invalid
				}
			}
		}
		obstacles, ok := room["obstacles_milli"].([]any)
		if !ok || len(obstacles) > 16 {
			return store.Invalid
		}
		if index >= 8 && len(obstacles) > 0 {
			return store.Invalid
		}
		for _, rawObstacle := range obstacles {
			rectangle, ok := rawObstacle.([]any)
			if !ok || len(rectangle) != 4 || jsonInteger(rectangle[0]) < 500 || jsonInteger(rectangle[1]) < 500 || jsonInteger(rectangle[2]) <= 0 || jsonInteger(rectangle[3]) <= 0 || jsonInteger(rectangle[0])+jsonInteger(rectangle[2]) > (width-1)*1000 || jsonInteger(rectangle[1])+jsonInteger(rectangle[3]) > (height-1)*1000 {
				return store.Invalid
			}
		}
	}
	doors, ok := geometry["doors"].([]any)
	if !ok || len(doors) != 14 {
		return store.Invalid
	}
	expectedFrom := []int64{0, 8, 1, 9, 2, 10, 3, 11, 4, 12, 2, 13, 4, 14}
	expectedTo := []int64{8, 1, 9, 2, 10, 3, 11, 4, 12, 5, 13, 6, 14, 7}
	for index, raw := range doors {
		door, ok := raw.(map[string]any)
		if !ok || integer(door, "from") < 0 || integer(door, "from") >= 15 || integer(door, "to") < 0 || integer(door, "to") >= 15 || integer(door, "from") == integer(door, "to") {
			return store.Invalid
		}
		side := "east"
		if index >= 10 {
			side = "south"
		}
		if integer(door, "from") != expectedFrom[index] || integer(door, "to") != expectedTo[index] || text(door, "side") != side {
			return store.Invalid
		}
	}
	camera, ok := geometry["camera"].(map[string]any)
	if !ok || integer(camera, "view_width_milli_tiles") != 24000 || integer(camera, "view_height_milli_tiles") != 13500 || integer(camera, "damping_ms") < 1 || integer(camera, "damping_ms") > 200 {
		return store.Invalid
	}
	return nil
}

func jsonInteger(value any) int64 {
	number, ok := value.(json.Number)
	if !ok {
		return -1
	}
	result, err := number.Int64()
	if err != nil {
		return -1
	}
	return result
}
