package gameplay

import (
	"bytes"
	"errors"
	"os"
	"path/filepath"
	"strconv"

	"game-realtime-gm/backend/internal/pve/store"
)

func (service *Service) P4() bool {
	return service.Content.Rules.Version == "dc_ruins.v4.p4.1" || service.V5()
}

func loadP4(path string) (*Content, error) {
	content, err := loadP3(filepath.Join(filepath.Dir(path), "demo-v4-p3.json"))
	if err != nil {
		return nil, err
	}
	read := func(name string) (map[string]any, []byte, error) {
		data, readErr := os.ReadFile(filepath.Join(filepath.Dir(path), name))
		if readErr != nil {
			return nil, nil, readErr
		}
		value, readErr := DecodeJSON(data)
		if readErr != nil {
			return nil, nil, readErr
		}
		object, ok := value.(map[string]any)
		if !ok {
			return nil, nil, store.Invalid
		}
		return object, data, nil
	}
	document, data, err := read(filepath.Base(path))
	if err != nil {
		return nil, err
	}
	schema, _, err := read("demo-v4-p4.schema.json")
	if err != nil {
		return nil, err
	}
	if err = ValidateSchema(document, schema); err != nil {
		return nil, err
	}
	world, geometryData, err := read("world-v4-p4.json")
	if err != nil {
		return nil, err
	}
	worldSchema, _, err := read("world-v4-p4.schema.json")
	if err != nil {
		return nil, err
	}
	if err = ValidateSchema(world, worldSchema); err != nil {
		return nil, err
	}
	if store.Hash(bytes.ReplaceAll(geometryData, []byte("\r\n"), []byte("\n"))) != text(document["world"].(map[string]any), "geometry_sha256") {
		return nil, errors.New("P4 geometry fingerprint mismatch")
	}
	world["parameters"] = content.World["parameters"]
	world["p2_stage"], world["p3_stage"], world["p4_stage"] = true, true, true
	content.Document, content.Hash, content.World = document, store.Hash(bytes.ReplaceAll(data, []byte("\r\n"), []byte("\n"))), world
	content.WorldHash = store.Hash(Canonical(world))
	content.Rules = content.rules()
	content.Rules.Version = "dc_ruins.v4.p4.1"
	content.V3.Content, content.V3.World = "dc_content.v4.p4.1", "dc_rooms.v4.p4.1"
	content.FrameSchema, _, err = read("../contracts/frame-v4-p4.schema.json")
	if err != nil {
		return nil, err
	}
	content.OpenAPI, _, err = read("../contracts/openapi-v4-p4-runtime.json")
	if err != nil {
		return nil, err
	}
	if err = content.Rules.Validate(); err != nil {
		return nil, err
	}
	for _, kit := range []string{"ranger", "runesmith"} {
		for _, first := range content.Candidates(1, "growth_1", kit, nil) {
			for _, second := range content.Candidates(1, "growth_2", kit, []string{first}) {
				if len(content.Candidates(1, "growth_3", kit, []string{first, second})) != 3 {
					return nil, store.Invalid
				}
			}
		}
	}
	return content, nil
}

func (content *Content) P4World(seed int64) (map[string]any, string, error) {
	value, err := DecodeJSON(Canonical(content.World))
	if err != nil {
		return nil, "", err
	}
	world := value.(map[string]any)
	rooms := world["rooms"].([]any)
	roll := func(domain string) uint64 {
		number, _ := strconv.ParseUint(store.Hash([]byte(fmtSeed(seed) + ":" + domain))[:8], 16, 32)
		return number
	}
	for _, value := range rooms {
		room := value.(map[string]any)
		if variants, ok := room["variants"].([]any); ok {
			variant := variants[roll("layout:"+text(room, "id"))%uint64(len(variants))].(map[string]any)
			room["variant_id"], room["obstacles_milli"] = variant["id"], variant["obstacles_milli"]
			if content.V3.Protocol >= 6 {
				for _, key := range []string{"cover_points_milli", "trap_points_milli", "covers", "traps", "spawn_candidates_milli", "mandatory_cover_groups"} {
					room[key] = variant[key]
				}
				delete(room, "variants")
			}
		} else {
			room["variant_id"] = text(room, "id") + "_a"
		}
	}
	for index, connector := range map[int]int{6: 13, 7: 14} {
		exists := roll("branch:"+text(rooms[index].(map[string]any), "id"))%1000 < uint64(integer(content.Section("p4"), "branch_probability_milli"))
		rooms[index].(map[string]any)["exists"] = exists
		rooms[connector].(map[string]any)["exists"] = exists
	}
	doors := []any{}
	for _, value := range world["doors"].([]any) {
		door := value.(map[string]any)
		if rooms[integer(door, "from")].(map[string]any)["exists"] == true && rooms[integer(door, "to")].(map[string]any)["exists"] == true {
			doors = append(doors, value)
		}
	}
	world["doors"] = doors
	return world, store.Hash(Canonical(world)), nil
}
