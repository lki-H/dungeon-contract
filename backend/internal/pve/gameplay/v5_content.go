package gameplay

import (
	"bytes"
	"errors"
	"os"
	"path/filepath"
	"strings"

	"game-realtime-gm/backend/internal/pve/store"
)

func (service *Service) V5() bool {
	return service.Content.Rules.Version == "dc_ruins.v8.p5.1" || service.Content.Rules.Version == "dc_ruins.v5.p4.1" || service.P5()
}

func (service *Service) P5() bool {
	return service.Content.Rules.Version == "dc_ruins.v8.p5.1" || service.Content.Rules.Version == "dc_ruins.v7.p5.2" || service.Content.Rules.Version == "dc_ruins.v7.p5.1" || service.Content.Rules.Version == "dc_ruins.v5.p5.1" || (service.Content.Rules.Version == "dc_ruins.v6.p5.1" || service.Content.Rules.Version == "dc_ruins.v6.p5.2")
}

func loadV5(filename string) (*Content, error) {
	content, err := loadP4(filepath.Join(filepath.Dir(filename), "demo-v4-p4.json"))
	if err != nil {
		return nil, err
	}
	read := func(relative string) (map[string]any, []byte, error) {
		data, readErr := os.ReadFile(filepath.Join(filepath.Dir(filename), relative))
		if readErr != nil {
			return nil, nil, readErr
		}
		decoded, readErr := DecodeJSON(data)
		if readErr != nil {
			return nil, nil, readErr
		}
		object, valid := decoded.(map[string]any)
		if !valid {
			return nil, nil, store.Invalid
		}
		return object, data, nil
	}
	profile := filepath.Base(filename)
	if profile != "demo-v5-p4.json" && profile != "demo-v5-p5.json" {
		return nil, store.Invalid
	}
	document, data, err := read(profile)
	if err != nil {
		return nil, err
	}
	schema, _, err := read(strings.TrimSuffix(profile, ".json") + ".schema.json")
	if err != nil {
		return nil, err
	}
	if err = ValidateSchema(document, schema); err != nil {
		return nil, err
	}
	parameters, parameterData, err := read("parameters-v5-p4.json")
	if err != nil {
		return nil, err
	}
	parameterSchema, _, err := read("parameters-v5-p4.schema.json")
	if err != nil {
		return nil, err
	}
	if err = ValidateSchema(parameters, parameterSchema); err != nil {
		return nil, err
	}
	world, worldData, err := read("world-v5-p4.json")
	if err != nil {
		return nil, err
	}
	worldSchema, _, err := read("world-v5-p4.schema.json")
	if err != nil {
		return nil, err
	}
	if err = ValidateSchema(world, worldSchema); err != nil {
		return nil, err
	}
	parameterHash := store.Hash(bytes.ReplaceAll(parameterData, []byte("\r\n"), []byte("\n")))
	if parameterHash != text(document, "parameters_sha256") || store.Hash(bytes.ReplaceAll(worldData, []byte("\r\n"), []byte("\n"))) != text(document["world"].(map[string]any), "geometry_sha256") {
		return nil, errors.New("v5 frozen source fingerprint mismatch")
	}
	world["parameters"] = parameters
	world["p2_stage"], world["p3_stage"], world["p4_stage"], world["v5_stage"] = true, true, true, true
	content.Document, content.Hash, content.World = document, store.Hash(bytes.ReplaceAll(data, []byte("\r\n"), []byte("\n"))), world
	content.WorldHash = store.Hash(Canonical(world))
	content.V3 = &VersionBinding{Protocol: 5, Operation: "dc_ruins.v5", Content: text(document["versions"].(map[string]any), "content"), World: "dc_rooms.v5.p4.1", ParametersHash: parameterHash}
	content.Rules = content.rules()
	content.Rules.Version = text(document, "definition_id")
	frameName := "../contracts/frame-v5-p4.schema.json"
	if profile == "demo-v5-p5.json" {
		frameName = "../contracts/frame-v5-p5.schema.json"
	}
	content.FrameSchema, _, err = read(frameName)
	if err != nil {
		return nil, err
	}
	apiName := "../contracts/openapi-v5-p4-runtime.json"
	if profile == "demo-v5-p5.json" {
		apiName = "../contracts/openapi-v5-p5-runtime.json"
	}
	content.OpenAPI, _, err = read(apiName)
	if err != nil {
		return nil, err
	}
	return content, content.Rules.Validate()
}

func validateV5Frame(frame Frame, allowTrial ...bool) error {
	validated := frame
	if len(allowTrial) > 0 && allowTrial[0] && frame.Summary != nil {
		copySummary := *frame.Summary
		copySummary.Eligible = false
		validated.Summary = &copySummary
	}
	if err := validateP3Frame(validated, 5); err != nil {
		return err
	}
	for _, fact := range frame.Facts {
		if fact.Kind != "damage" && fact.Kind != "kill" {
			continue
		}
		if strings.HasPrefix(fact.Target, "summon_") {
			return store.Forbidden
		}
		if fact.SpawnOrigin != "main_wave" && fact.SpawnOrigin != "branch_wave" && fact.SpawnOrigin != "boss_body" {
			return store.Forbidden
		}
	}
	return nil
}
