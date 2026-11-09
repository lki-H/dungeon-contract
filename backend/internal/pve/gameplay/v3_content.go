package gameplay

import (
	"bytes"
	"encoding/json"
	"errors"
	"os"
	"path/filepath"

	"game-realtime-gm/backend/internal/pve/store"
	"game-realtime-gm/backend/internal/pve/v3"
)

type VersionBinding struct {
	Protocol       int    `json:"protocol_version"`
	Operation      string `json:"operation_version"`
	Content        string `json:"content_version"`
	World          string `json:"world_version"`
	ParametersHash string `json:"parameters_hash"`
}

func loadV3P0(path string) (*Content, error) {
	multiplayerStage := filepath.Base(path) == "demo-v4-p2-mp.json"
	p2Stage := filepath.Base(path) == "demo-v4-p2-1.json" || multiplayerStage
	weaponStage := filepath.Base(path) == "demo-v4-s2.json" || p2Stage
	syncStage := filepath.Base(path) == "demo-v4-s1.json" || weaponStage
	p1 := filepath.Base(path) == "demo-v3-p1.json" || syncStage
	protocol, operationVersion, contentVersion, worldVersion, trialVersion, folder := 3, v3.OperationVersion, v3.ContentVersion, v3.WorldVersion, v3.TrialVersion, "planned-v3"
	if syncStage {
		protocol, operationVersion, contentVersion, worldVersion, trialVersion, folder = 4, "dc_ruins.v4", "dc_content.v4.draft1", "dc_rooms.v4.draft1", "trial.v4.draft1", "planned-v4"
	}
	if p2Stage {
		contentVersion, worldVersion, trialVersion = "dc_content.v4.draft3", "dc_rooms.v4.draft2", "trial.v4.draft3"
	}
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	value, err := DecodeJSON(data)
	if err != nil {
		return nil, err
	}
	document, ok := value.(map[string]any)
	schemaName := "demo-v3-p0.schema.json"
	if p1 {
		schemaName = "demo-v3-p1.schema.json"
	}
	if syncStage {
		schemaName = "demo-v4-s1.schema.json"
	}
	if weaponStage && !p2Stage {
		schemaName = "demo-v4-s2.schema.json"
	}
	if p2Stage {
		schemaName = "demo-v4-p2-1.schema.json"
	}
	if multiplayerStage {
		schemaName = "demo-v4-p2-mp.schema.json"
	}
	schemaData, err := os.ReadFile(filepath.Join(filepath.Dir(path), schemaName))
	if err != nil {
		return nil, err
	}
	schemaValue, err := DecodeJSON(schemaData)
	if err != nil {
		return nil, err
	}
	schema := schemaValue.(map[string]any)
	if err := ValidateSchema(value, schema); err != nil {
		return nil, err
	}
	stage, ruleVersion := "p0_control_only", "dc_ruins.v3.p0"
	if p1 {
		stage, ruleVersion = "p1_single_player", "dc_ruins.v3.p1"
	}
	if syncStage {
		stage, ruleVersion = "sync_foundation", "dc_ruins.v4.s1"
	}
	if weaponStage && !p2Stage {
		stage, ruleVersion = "weapon_stage", "dc_ruins.v4.s2"
	}
	if p2Stage {
		stage, ruleVersion = "room_camera_projectile_stage", "dc_ruins.v4.p2.1"
	}
	if multiplayerStage {
		stage, ruleVersion = "multiplayer_sync_stage", "dc_ruins.v4.p2.multiplayer"
	}
	expectedDefinition := operationVersion
	if p2Stage {
		expectedDefinition = ruleVersion
	}
	if !ok || text(document, "definition_id") != expectedDefinition || integer(document, "protocol_version") != int64(protocol) || text(document, "lifecycle_stage") != stage {
		return nil, errors.New("invalid v3 P0 runtime definition")
	}
	versions, ok := document["versions"].(map[string]any)
	if !ok || text(versions, "operation") != operationVersion || text(versions, "content") != contentVersion || text(versions, "world") != worldVersion || text(versions, "trial") != trialVersion || integer(versions, "battle_protocol") != int64(protocol) || integer(versions, "control_schema") != 2 {
		return nil, errors.New("v3 P0 runtime versions mismatch")
	}
	if p2Stage && text(document, "parameters_path") != "../config/parameters-v4-p2-1.json" || !p2Stage && text(document, "parameters_path") != "../contracts/"+folder+"/parameters.json" {
		return nil, errors.New("v3 parameter source path mismatch")
	}
	parameterPath := filepath.Join(filepath.Dir(path), "..", "contracts", folder, "parameters.json")
	if p2Stage {
		parameterPath = filepath.Join(filepath.Dir(path), "..", "config", "parameters-v4-p2-1.json")
	}
	parametersData, err := os.ReadFile(parameterPath)
	if err != nil {
		return nil, err
	}
	parametersHash := store.Hash(bytes.ReplaceAll(parametersData, []byte("\r\n"), []byte("\n")))
	if parametersHash != text(document, "parameters_sha256") {
		return nil, errors.New("v3 parameter hash mismatch")
	}
	parametersValue, err := DecodeJSON(parametersData)
	if err != nil {
		return nil, err
	}
	parameters, ok := parametersValue.(map[string]any)
	if !ok {
		return nil, errors.New("v3 parameters must be an object")
	}
	parametersSchemaPath := filepath.Join(filepath.Dir(path), "..", "contracts", folder, "parameters.schema.json")
	if p2Stage {
		parametersSchemaPath = filepath.Join(filepath.Dir(path), "parameters-v4-p2-1.schema.json")
	}
	parametersSchemaData, err := os.ReadFile(parametersSchemaPath)
	if err != nil {
		return nil, err
	}
	parametersSchema, err := DecodeJSON(parametersSchemaData)
	if err != nil {
		return nil, err
	}
	if err := ValidateSchema(parameters, parametersSchema.(map[string]any)); err != nil {
		return nil, err
	}
	parameterStatus := "planned_not_loaded"
	if p2Stage {
		parameterStatus = "stage_runtime_subset"
	}
	if parameters["published"] != false || text(parameters, "status") != parameterStatus || !bytes.Equal(Canonical(versions), Canonical(parameters["versions"])) {
		return nil, errors.New("v3 parameter publication/version mismatch")
	}
	for _, key := range []string{"operation", "authority", "trial", "world"} {
		if _, ok := document[key].(map[string]any); !ok {
			return nil, errors.New("v3 runtime section missing")
		}
	}
	operation, authority := document["operation"].(map[string]any), document["authority"].(map[string]any)
	if text(operation, "version") != operationVersion || integer(operation, "hard_deadline_ms") != 1800000 || integer(operation, "wall_limit_ms") != 3600000 || integer(authority, "tick_hz") != 30 || integer(authority, "heartbeat_ms") >= integer(authority, "lease_ms") {
		return nil, errors.New("v3 clock configuration mismatch")
	}
	for key := range authority {
		if integer(authority, key) < 1 {
			return nil, errors.New("v3 authority budgets must be positive")
		}
	}
	for key, maximum := range map[string]int64{"max_frame_bytes": 65536, "max_frame_facts": 128, "max_key_frames": 4096, "max_facts": 10000, "reorder_window": 128, "max_pending_frames": 256, "max_pending_bytes": 8388608} {
		if integer(authority, key) > maximum {
			return nil, errors.New("v3 authority budget exceeds protocol limit")
		}
	}
	if integer(authority, "max_key_frames") < integer(operation, "hard_deadline_ms")/1000 || integer(authority, "max_facts") < integer(authority, "max_key_frames") {
		return nil, errors.New("P0 checkpoints exceed the run budget")
	}
	content := &Content{Document: document, Hash: store.Hash(bytes.ReplaceAll(data, []byte("\r\n"), []byte("\n"))), World: document["world"].(map[string]any), V3: &VersionBinding{Protocol: protocol, Operation: operationVersion, Content: contentVersion, World: worldVersion, ParametersHash: parametersHash}}
	content.WorldHash = store.Hash(Canonical(content.World))
	content.Rules = content.rules()
	if text(document, "control_rule_version") != ruleVersion {
		return nil, errors.New("P0 control rule version mismatch")
	}
	content.Rules.Version = text(document, "control_rule_version")
	if p1 {
		geometryName := "world-v3-p1.json"
		if p2Stage {
			geometryName = "world-v4-p2-1.json"
		}
		geometryData, err := os.ReadFile(filepath.Join(filepath.Dir(path), geometryName))
		if err != nil {
			return nil, err
		}
		geometry, err := DecodeJSON(geometryData)
		if err != nil {
			return nil, err
		}
		if p2Stage {
			geometrySchemaData, readErr := os.ReadFile(filepath.Join(filepath.Dir(path), "world-v4-p2-1.schema.json"))
			if readErr != nil {
				return nil, readErr
			}
			geometrySchema, decodeErr := DecodeJSON(geometrySchemaData)
			if decodeErr != nil {
				return nil, decodeErr
			}
			if err := ValidateSchema(geometry, geometrySchema.(map[string]any)); err != nil {
				return nil, err
			}
			if text(document, "combat_parameters_path") != text(document, "parameters_path") || text(document, "combat_parameters_sha256") != parametersHash {
				return nil, errors.New("P2.1 combat parameter binding mismatch")
			}
		}
		if store.Hash(bytes.ReplaceAll(geometryData, []byte("\r\n"), []byte("\n"))) != text(document["world"].(map[string]any), "geometry_sha256") {
			return nil, errors.New("P1 geometry hash mismatch")
		}
		content.World = geometry.(map[string]any)
		if p2Stage {
			err = ValidateP2Geometry(content.World)
		} else {
			err = ValidateP1Geometry(content.World)
		}
		if err != nil {
			return nil, err
		}
		if syncStage && !weaponStage {
			combatData, readErr := os.ReadFile(filepath.Join(filepath.Dir(path), "..", "contracts", "planned-v3", "parameters.json"))
			if readErr != nil {
				return nil, readErr
			}
			if store.Hash(bytes.ReplaceAll(combatData, []byte("\r\n"), []byte("\n"))) != text(document, "combat_parameters_sha256") {
				return nil, errors.New("S1 combat parameter hash mismatch")
			}
			combatValue, readErr := DecodeJSON(combatData)
			if readErr != nil {
				return nil, readErr
			}
			parameters = combatValue.(map[string]any)
		}
		content.World["parameters"] = parameters
		content.WorldHash = store.Hash(Canonical(content.World))
		if content.Rules.Reinforcements != 0 || content.Rules.SuccessReward != 0 || content.Rules.FailureReward != 0 || content.Rules.AbortReward != 0 {
			return nil, errors.New("P1 stage must not publish gameplay rewards or recovery budgets")
		}
	}
	if len(content.Rules.Tasks) != 0 {
		return nil, errors.New("P0 does not publish gameplay contracts")
	}
	frameName := "frame-v3-p0.schema.json"
	if syncStage {
		frameName = "frame-v4-s1.schema.json"
	}
	frameData, err := os.ReadFile(filepath.Join(filepath.Dir(path), "..", "contracts", frameName))
	if err != nil {
		return nil, err
	}
	frameValue, err := DecodeJSON(frameData)
	if err != nil {
		return nil, err
	}
	content.FrameSchema = frameValue.(map[string]any)
	contractName := "openapi-v3-p0-runtime.json"
	if p1 {
		contractName = "openapi-v3-p1-runtime.json"
	}
	if syncStage {
		contractName = "openapi-v4-s1-runtime.json"
	}
	if weaponStage {
		contractName = "openapi-v4-s2-runtime.json"
	}
	if p2Stage {
		contractName = "openapi-v4-p2-1-runtime.json"
	}
	if multiplayerStage {
		contractName = "openapi-v4-p2-mp-runtime.json"
	}
	contractData, err := os.ReadFile(filepath.Join(filepath.Dir(path), "..", "contracts", contractName))
	if err != nil {
		return nil, err
	}
	contractValue, err := DecodeJSON(contractData)
	if err != nil {
		return nil, err
	}
	content.OpenAPI = contractValue.(map[string]any)
	if err := content.Rules.Validate(); err != nil {
		return nil, err
	}
	return content, nil
}

func (binding *VersionBinding) validFor(content *Content) bool {
	if content.V3 == nil {
		return binding == nil
	}
	return binding != nil && content.V3 != nil && *binding == *content.V3
}

func validateV3Frame(data []byte, content *Content) (Frame, error) {
	var frame Frame
	value, err := DecodeJSON(data)
	if err != nil || len(data) > 65536 {
		return frame, store.Invalid
	}
	if err := ValidateSchema(value, content.FrameSchema); err != nil {
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
	if content.Rules.Version == "dc_ruins.v8.p5.1" {
		return frame, validateV8Frame(frame)
	}
	if content.Rules.Version == "dc_ruins.v7.p5.1" || content.Rules.Version == "dc_ruins.v7.p5.2" {
		return frame, validateV7Frame(frame)
	}
	if content.Rules.Version == "dc_ruins.v6.p5.2" {
		return frame, validateV6Frame(frame)
	}
	if content.Rules.Version == "dc_ruins.v5.p4.1" || content.Rules.Version == "dc_ruins.v5.p5.1" {
		return frame, validateV5Frame(frame, content.Rules.Version == "dc_ruins.v5.p5.1")
	}
	if content.Rules.Version == "dc_ruins.v4.p3.1" || content.Rules.Version == "dc_ruins.v4.p4.1" {
		return frame, validateP3Frame(frame)
	}
	if frame.Schema != content.ProtocolVersion() || frame.Source != "unity" || frame.WallElapsed != frame.Elapsed+frame.ManualPause || (content.ProtocolVersion() == 3 && frame.ManualPause != 0) || frame.Terminal != nil || frame.Summary != nil || len(frame.Facts) == 0 {
		return frame, store.Invalid
	}
	for _, fact := range frame.Facts {
		if fact.Kind != "phase" || fact.Actor != nil || fact.Action != nil || fact.Amount != 0 || fact.LifeGeneration != 0 || fact.DownedGeneration != 0 || fact.EventGameMS > frame.Elapsed || fact.EventGameMS < 0 || len(fact.Contributors) != 0 || string(fact.Proof) != "null" {
			return frame, store.Invalid
		}
	}
	return frame, nil
}
