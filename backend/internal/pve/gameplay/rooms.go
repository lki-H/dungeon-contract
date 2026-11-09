package gameplay

import (
	"encoding/json"
	"errors"
)

func (content *Content) validateRooms() error {
	if text(content.World, "definition_id") != "dc_world.v2" {
		return errors.New("version two requires the room world")
	}
	physics := content.World["physics"].(map[string]any)
	if integer(physics, "sweep_step_milli") <= 0 || integer(physics, "sweep_step_milli") > integer(physics, "projectile_radius_milli")*2 {
		return errors.New("unsafe collision sweep")
	}
	view := content.World["view"].(map[string]any)
	if integer(view, "reference_width") != 384 || integer(view, "reference_height") != 216 || integer(view, "snapshot_hz") != 30 {
		return errors.New("room presentation dimensions or frequency mismatch")
	}
	rooms := objects(content.World, "rooms")
	seen := map[string]bool{}
	for _, room := range rooms {
		id := text(room, "id")
		width, height := int(integer(room, "width")), int(integer(room, "height"))
		if seen[id] || id == "" || width < 8 || width > 18 || height < 6 || height > 10 {
			return errors.New("room identity or dimensions invalid")
		}
		seen[id] = true
		blocked := map[[2]int]bool{}
		for _, raw := range room["obstacles"].([]any) {
			rectangle := raw.([]any)
			coordinates := [4]int{}
			for index, coordinate := range rectangle {
				value, _ := coordinate.(json.Number).Int64()
				coordinates[index] = int(value)
			}
			if coordinates[0]+coordinates[2] >= width-1 || coordinates[1]+coordinates[3] >= height-1 {
				return errors.New("obstacle intersects room boundary")
			}
			for horizontal := coordinates[0]; horizontal < coordinates[0]+coordinates[2]; horizontal++ {
				for vertical := coordinates[1]; vertical < coordinates[1]+coordinates[3]; vertical++ {
					blocked[[2]int{horizontal, vertical}] = true
				}
			}
		}
		point := func(raw any) [2]int {
			array := raw.([]any)
			horizontal, _ := array[0].(json.Number).Int64()
			vertical, _ := array[1].(json.Number).Int64()
			return [2]int{int(horizontal), int(vertical)}
		}
		start := point(room["spawn"])
		valid := func(location [2]int) bool {
			return location[0] >= 1 && location[0] < width-1 && location[1] >= 1 && location[1] < height-1 && !blocked[location]
		}
		if !valid(start) {
			return errors.New("spawn intersects room geometry")
		}
		visited := map[[2]int]bool{start: true}
		queue := [][2]int{start}
		for len(queue) > 0 {
			current := queue[0]
			queue = queue[1:]
			for _, direction := range [][2]int{{1, 0}, {-1, 0}, {0, 1}, {0, -1}} {
				next := [2]int{current[0] + direction[0], current[1] + direction[1]}
				if valid(next) && !visited[next] {
					visited[next] = true
					queue = append(queue, next)
				}
			}
		}
		if len(visited) != (width-2)*(height-2)-len(blocked) {
			return errors.New("room contains disconnected floor")
		}
		if room["objective"] != nil && !visited[point(room["objective"])] {
			return errors.New("required interaction is unreachable")
		}
		for _, trap := range room["trap_points"].([]any) {
			location := point(trap)
			if !visited[location] || location == start || room["objective"] != nil && location == point(room["objective"]) {
				return errors.New("trap intersects protected room point")
			}
		}
	}
	for index, room := range objects(content.Document, "rooms") {
		if text(room, "id") != text(rooms[index], "id") {
			return errors.New("world and operation room order mismatch")
		}
	}
	doors := objects(content.World, "doors")
	for index, door := range doors {
		origin, target := integer(door, "from"), integer(door, "to")
		if origin < 0 || target >= int64(len(rooms)) || origin == target {
			return errors.New("invalid room connection")
		}
		if index < 5 && (origin != int64(index) || target != int64(index+1)) {
			return errors.New("main route is not ordered and connected")
		}
	}
	return nil
}
