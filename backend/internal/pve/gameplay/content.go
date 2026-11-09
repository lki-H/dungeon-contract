package gameplay

import (
	"bytes"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"
	"sort"

	"game-realtime-gm/backend/internal/pve/store"
	"game-realtime-gm/backend/internal/pve/task"
)

type Content struct {
	V3          *VersionBinding
	World       map[string]any
	WorldHash   string
	Document    map[string]any
	Hash        string
	FrameSchema map[string]any
	OpenAPI     map[string]any
	Rules       task.Rules
}

func LoadContent(path string) (*Content, error) {
	if filepath.Base(path) == "demo-v8-p5.json" {
		return loadV8(path)
	}
	if filepath.Base(path) == "demo-v7-p5.json" {
		return loadV7(path)
	}
	if filepath.Base(path) == "demo-v6-p5.json" {
		return loadV6(path)
	}
	if filepath.Base(path) == "demo-v5-p4.json" || filepath.Base(path) == "demo-v5-p5.json" {
		return loadV5(path)
	}
	if filepath.Base(path) == "demo-v4-p4.json" {
		return loadP4(path)
	}
	if filepath.Base(path) == "demo-v4-p3.json" {
		return loadP3(path)
	}
	if filepath.Base(path) == "demo-v3-p0.json" || filepath.Base(path) == "demo-v3-p1.json" || filepath.Base(path) == "demo-v4-s1.json" || filepath.Base(path) == "demo-v4-s2.json" || filepath.Base(path) == "demo-v4-p2-1.json" || filepath.Base(path) == "demo-v4-p2-mp.json" {
		return loadV3P0(path)
	}
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	value, err := DecodeJSON(data)
	if err != nil {
		return nil, err
	}
	schemaName, worldName, worldSchemaName, frameName, contractName := "demo-config.schema.json", "p1-world.v1.json", "p1-world.schema.json", "frame.schema.json", "openapi-runtime.json"
	if filepath.Base(path) == "demo-v2.json" {
		schemaName, worldName, worldSchemaName, frameName, contractName = "demo-v2.schema.json", "world-v2.json", "world-v2.schema.json", "frame-v2.schema.json", "openapi-v2-runtime.json"
	}
	schemaData, err := os.ReadFile(filepath.Join(filepath.Dir(path), schemaName))
	if err != nil {
		return nil, err
	}
	schema, err := DecodeJSON(schemaData)
	if err != nil {
		return nil, err
	}
	if err := ValidateSchema(value, schema.(map[string]any)); err != nil {
		return nil, err
	}
	frameData, err := os.ReadFile(filepath.Join(filepath.Dir(path), "..", "contracts", frameName))
	if err != nil {
		return nil, err
	}
	frameSchema, err := DecodeJSON(frameData)
	if err != nil {
		return nil, err
	}
	document := value.(map[string]any)
	content := &Content{Document: document, Hash: store.Hash(bytes.ReplaceAll(data, []byte("\r\n"), []byte("\n"))), FrameSchema: frameSchema.(map[string]any)}
	contractBytes, err := os.ReadFile(filepath.Join(filepath.Dir(path), "..", "contracts", contractName))
	if err != nil {
		return nil, err
	}
	contract, err := DecodeJSON(contractBytes)
	if err != nil {
		return nil, err
	}
	content.OpenAPI = contract.(map[string]any)
	worldBytes, err := os.ReadFile(filepath.Join(filepath.Dir(path), worldName))
	if err != nil {
		return nil, err
	}
	world, err := DecodeJSON(worldBytes)
	if err != nil {
		return nil, err
	}
	worldSchemaBytes, err := os.ReadFile(filepath.Join(filepath.Dir(path), worldSchemaName))
	if err != nil {
		return nil, err
	}
	worldSchema, err := DecodeJSON(worldSchemaBytes)
	if err != nil {
		return nil, err
	}
	if err := ValidateSchema(world, worldSchema.(map[string]any)); err != nil {
		return nil, err
	}
	content.World = world.(map[string]any)
	content.WorldHash = store.Hash(bytes.ReplaceAll(worldBytes, []byte("\r\n"), []byte("\n")))
	if err := content.validateWorld(); err != nil {
		return nil, err
	}
	if err := content.validate(); err != nil {
		return nil, err
	}
	content.Rules = content.rules()
	return content, content.Rules.Validate()
}

func (content *Content) validateWorld() error {
	if content.ProtocolVersion() == 2 {
		return content.validateRooms()
	}
	encounter := content.World["encounter"].(map[string]any)
	rows := encounter["door_rows"].([]any)
	first, _ := rows[0].(json.Number).Int64()
	second, _ := rows[1].(json.Number).Int64()
	if first != 5 || second != 6 {
		return errors.New("P1 fixed route requires door rows 5 and 6")
	}
	for _, key := range []string{"boss_spawn", "supply_position"} {
		point := encounter[key].([]any)
		horizontal, _ := point[0].(json.Number).Int64()
		vertical, _ := point[1].(json.Number).Int64()
		if horizontal < 2 || horizontal > 14 || vertical < 2 || vertical > 10 {
			return errors.New("P1 encounter point intersects a wall")
		}
	}
	if integer(content.World["physics"].(map[string]any), "sweep_step_milli") > integer(content.World["physics"].(map[string]any), "projectile_radius_milli")*2 {
		return errors.New("P1 projectile sweep can skip collisions")
	}
	return nil
}

func integer(object map[string]any, key string) int64 {
	number, _ := object[key].(json.Number)
	value, _ := number.Int64()
	return value
}
func text(object map[string]any, key string) string { value, _ := object[key].(string); return value }
func objects(document map[string]any, key string) []map[string]any {
	values, _ := document[key].([]any)
	result := []map[string]any{}
	for _, value := range values {
		result = append(result, value.(map[string]any))
	}
	return result
}
func stringsOf(value any) []string {
	array, _ := value.([]any)
	result := []string{}
	for _, child := range array {
		result = append(result, child.(string))
	}
	return result
}
func (content *Content) Section(key string) map[string]any {
	return content.Document[key].(map[string]any)
}
func (content *Content) Millis(key string) int64 { return integer(content.Section("authority"), key) }

func (content *Content) ProtocolVersion() int {
	return int(integer(content.Document, "protocol_version"))
}

func (content *Content) validate() error {
	for key, count := range map[string]int{"loadouts": 2, "enemies": 5, "tasks": 6, "upgrades": 10, "rooms": 6, "objectives": 3} {
		values := objects(content.Document, key)
		if len(values) != count {
			return errors.New("content cardinality mismatch")
		}
		seen := map[string]bool{}
		for _, value := range values {
			id := text(value, "id")
			if id == "" || seen[id] {
				return errors.New("duplicate content ID")
			}
			seen[id] = true
		}
	}
	operation := content.Section("operation")
	authority := content.Section("authority")
	player := content.Section("player")
	if text(operation, "id") != "dc_ruins" || text(operation, "difficulty") != "standard" || text(operation, "version") != text(content.Document, "definition_id") {
		return errors.New("operation definition mismatch")
	}
	for _, section := range []map[string]any{operation, authority, player} {
		for key, value := range section {
			if number, ok := value.(json.Number); ok {
				amount, _ := number.Int64()
				if amount < 0 || ((key != "abort_reward" && key != "minimum_dodge_cooldown_ms") && amount == 0) {
					return errors.New("invalid positive content parameter")
				}
			}
		}
	}
	if integer(authority, "heartbeat_ms") >= integer(authority, "lease_ms") || integer(authority, "lease_ms") >= integer(authority, "technical_wait_ms") || integer(authority, "spawn_delay_ms") >= integer(authority, "spawn_ack_deadline_ms") || integer(player, "dodge_invulnerable_ms") > integer(player, "dodge_duration_ms") {
		return errors.New("content timing order")
	}
	if operation["friendly_fire"] != false || operation["player_blocking"] != false || operation["allow_late_join"] != false {
		return errors.New("unsupported content policy")
	}
	for key, maximum := range map[string]int64{"max_frame_facts": 128, "max_frame_bytes": 65536, "max_key_frames": 4096, "max_facts": 10000, "reorder_window": 128, "max_pending_frames": 256, "max_pending_bytes": 8388608} {
		if integer(authority, key) > maximum {
			return errors.New("content exceeds protocol safety budget")
		}
	}
	for _, objectiveSet := range append([][]map[string]any{objects(content.Document, "objectives")}, taskObjectiveSets(content.Document)...) {
		if err := validateDependencies(objectiveSet); err != nil {
			return err
		}
	}
	rooms := objects(content.Document, "rooms")
	objectives := map[string]bool{}
	for _, objective := range objects(content.Document, "objectives") {
		objectives[text(objective, "id")] = true
	}
	nodes := map[string]bool{}
	for _, room := range rooms {
		if id := text(room, "objective_id"); id != "" && !objectives[id] {
			return errors.New("room objective missing")
		}
		if id := text(room, "safe_node_id"); id != "" {
			if nodes[id] {
				return errors.New("duplicate choice node")
			}
			nodes[id] = true
		}
		if room["origin"] == nil {
			return errors.New("room origin missing")
		}
	}
	if len(nodes) != 3 {
		return errors.New("three safe nodes required")
	}
	scaling := objects(content.Document, "scaling")
	counts := map[int64]bool{}
	for _, entry := range scaling {
		count := integer(entry, "players")
		if count < 1 || count > 4 || counts[count] || integer(entry, "count_milli") <= 0 || integer(entry, "health_milli") <= 0 {
			return errors.New("invalid player scaling")
		}
		counts[count] = true
	}
	if len(counts) != 4 {
		return errors.New("missing player scaling")
	}
	enemies := map[string]bool{}
	for _, enemy := range objects(content.Document, "enemies") {
		enemies[text(enemy, "id")] = true
		if integer(enemy, "max_health") <= 0 || (text(enemy, "id") != "boss" && integer(enemy, "attack_interval_ms") <= 0) || (text(enemy, "id") == "boss" && (integer(enemy, "stomp_interval_ms") <= 0 || integer(enemy, "fan_interval_ms") <= 0 || integer(enemy, "summon_interval_ms") <= 0)) {
			return errors.New("enemy parameters invalid")
		}
	}
	for _, room := range rooms {
		origin := room["origin"].([]any)
		if len(origin) != 2 {
			return errors.New("room origin dimensions")
		}
		for _, wave := range objects(room, "waves") {
			mix, weights := stringsOf(wave["mix"]), wave["weights"].([]any)
			if integer(wave, "count") <= 0 || len(mix) != len(weights) || len(mix) == 0 {
				return errors.New("wave budget or weights mismatch")
			}
			for index, id := range mix {
				weight, _ := weights[index].(json.Number).Int64()
				if !enemies[id] || weight <= 0 {
					return errors.New("unknown wave enemy or invalid weight")
				}
			}
		}
	}
	world := content.Section("world")
	if content.ProtocolVersion() == 1 && (integer(world, "width_tiles") != 96 || integer(world, "height_tiles") != 12) {
		return errors.New("fixed route dimensions invalid")
	}
	for index, room := range rooms {
		origin := room["origin"].([]any)
		horizontal, _ := origin[0].(json.Number).Int64()
		vertical, _ := origin[1].(json.Number).Int64()
		if content.ProtocolVersion() == 1 && (horizontal != int64(index*16) || vertical != 0) {
			return errors.New("fixed main route rooms disconnected")
		}
	}
	upgrades := map[string]map[string]any{}
	for _, upgrade := range objects(content.Document, "upgrades") {
		upgrades[text(upgrade, "id")] = upgrade
	}
	priority := content.Section("trial")["candidate_priority"].(map[string]any)
	for _, loadout := range objects(content.Document, "loadouts") {
		id := text(loadout, "id")
		legal := 0
		for _, upgrade := range upgrades {
			if text(upgrade, "scope") == "common" || text(upgrade, "scope") == id {
				legal++
			}
		}
		if legal < 5 {
			return errors.New("insufficient upgrade candidates")
		}
		for _, candidate := range stringsOf(priority[id]) {
			upgrade, exists := upgrades[candidate]
			if !exists || (text(upgrade, "scope") != "common" && text(upgrade, "scope") != id) {
				return errors.New("invalid trial candidate")
			}
		}
	}
	return nil
}
func taskObjectiveSets(document map[string]any) [][]map[string]any {
	result := [][]map[string]any{}
	for _, definition := range objects(document, "tasks") {
		result = append(result, objects(definition, "objectives"))
	}
	return result
}
func validateDependencies(values []map[string]any) error {
	byID := map[string]map[string]any{}
	for _, value := range values {
		id := text(value, "id")
		if byID[id] != nil {
			return errors.New("duplicate objective")
		}
		byID[id] = value
	}
	visiting, visited := map[string]bool{}, map[string]bool{}
	var visit func(string) bool
	visit = func(id string) bool {
		if visiting[id] || byID[id] == nil {
			return false
		}
		if visited[id] {
			return true
		}
		visiting[id] = true
		for _, dependency := range stringsOf(byID[id]["depends_on"]) {
			if !visit(dependency) {
				return false
			}
		}
		visiting[id] = false
		visited[id] = true
		return true
	}
	for id := range byID {
		if !visit(id) {
			return errors.New("objective dependency missing or cyclic")
		}
	}
	return nil
}
func (content *Content) rules() task.Rules {
	operation, authority := content.Section("operation"), content.Section("authority")
	rules := task.Rules{Version: text(operation, "version"), Operation: text(operation, "id"), Difficulty: text(operation, "difficulty"), Duration: int(integer(operation, "hard_deadline_ms") / 1000), Reinforcements: int(integer(operation, "reinforcements")), SuccessReward: integer(operation, "success_reward"), FailureReward: integer(operation, "failure_reward"), AbortReward: integer(operation, "abort_reward"), FillWait: int(integer(operation, "fill_wait_ms") / 1000), Confirmation: int(integer(operation, "confirmation_ms") / 1000), Loading: int(integer(operation, "loading_ms") / 1000), Reconnect: int(integer(operation, "reconnect_ms") / 1000), Gap: int(integer(authority, "event_gap_ms") / 1000), Spawn: int(integer(authority, "spawn_ack_deadline_ms") / 1000)}
	convert := func(value map[string]any) task.Objective {
		target := text(value, "target_id")
		if target == "" {
			target = text(value, "target")
		}
		if target == "eligible_enemy" {
			target = ""
		}
		persistent, _ := value["persistent"].(bool)
		scope := text(value, "scope")
		if scope == "" {
			scope = "team"
		}
		return task.Objective{Key: text(value, "id"), Type: text(value, "kind"), Required: integer(value, "required"), Scope: scope, Persistent: persistent, TargetID: target, DependsOn: stringsOf(value["depends_on"])}
	}
	for _, value := range objects(content.Document, "objectives") {
		rules.Objectives = append(rules.Objectives, convert(value))
	}
	for _, value := range objects(content.Document, "tasks") {
		locked, _ := value["locked"].(bool)
		success, _ := value["requires_success"].(bool)
		deaths, _ := value["no_deaths"].(bool)
		knockdowns, _ := value["no_knockdowns"].(bool)
		definition := task.Definition{Key: text(value, "id"), Version: text(value, "version"), Operation: rules.Operation, Reward: integer(value, "reward"), Next: text(value, "next"), Locked: locked, RequiresSuccess: success, NoDeaths: deaths, NoKnockdowns: knockdowns}
		for _, objective := range objects(value, "objectives") {
			definition.Objectives = append(definition.Objectives, convert(objective))
		}
		rules.Tasks = append(rules.Tasks, definition)
	}
	return rules
}
func (content *Content) Candidates(seed int64, node, loadout string, selected []string) []string {
	chosen := map[string]bool{}
	for _, id := range selected {
		chosen[id] = true
	}
	ids := []string{}
	for _, upgrade := range objects(content.Document, "upgrades") {
		id := text(upgrade, "id")
		if !chosen[id] && (text(upgrade, "scope") == "common" || text(upgrade, "scope") == loadout) {
			ids = append(ids, id)
		}
	}
	sort.Slice(ids, func(first, second int) bool {
		return store.Hash([]byte(fmtSeed(seed)+":"+node+":"+loadout+":"+ids[first])) < store.Hash([]byte(fmtSeed(seed)+":"+node+":"+loadout+":"+ids[second]))
	})
	if len(ids) > 3 {
		ids = ids[:3]
	}
	return ids
}
func fmtSeed(seed int64) string { data, _ := json.Marshal(seed); return string(data) }
