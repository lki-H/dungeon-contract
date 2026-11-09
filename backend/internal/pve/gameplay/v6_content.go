package gameplay

import (
	"bytes"
	"errors"
	"game-realtime-gm/backend/internal/pve/store"
	"os"
	"path/filepath"
)

// V6 retains task semantics while freezing a separate battle/content/world identity.
func loadV6(filename string) (*Content, error) {
	content, err := loadV5(filepath.Join(filepath.Dir(filename), "demo-v5-p5.json"))
	if err != nil {
		return nil, err
	}
	read := func(name string) (map[string]any, []byte, error) {
		data, e := os.ReadFile(filepath.Join(filepath.Dir(filename), name))
		if e != nil {
			return nil, nil, e
		}
		v, e := DecodeJSON(data)
		if e != nil {
			return nil, nil, e
		}
		o, ok := v.(map[string]any)
		if !ok {
			return nil, nil, store.Invalid
		}
		return o, data, nil
	}
	document, data, err := read("demo-v6-p5.json")
	if err != nil {
		return nil, err
	}
	schema, _, err := read("demo-v6-p5.schema.json")
	if err != nil {
		return nil, err
	}
	if err = ValidateSchema(document, schema); err != nil {
		return nil, err
	}
	parameters, pdata, err := read("parameters-v6-p5.json")
	if err != nil {
		return nil, err
	}
	world, wdata, err := read("world-v6-p5.json")
	if err != nil {
		return nil, err
	}
	for name, value := range map[string]map[string]any{"parameters": parameters, "world": world} {
		s, _, e := read(name + "-v6-p5.schema.json")
		if e != nil {
			return nil, e
		}
		if e = ValidateSchema(value, s); e != nil {
			return nil, e
		}
	}
	hash := func(b []byte) string { return store.Hash(bytes.ReplaceAll(b, []byte("\r\n"), []byte("\n"))) }
	ph := hash(pdata)
	if ph != text(document, "parameters_sha256") || hash(wdata) != text(document["world"].(map[string]any), "geometry_sha256") {
		return nil, errors.New("v6 frozen definition fingerprint mismatch")
	}
	world["parameters"] = parameters
	world["p2_stage"], world["p3_stage"], world["p4_stage"], world["v5_stage"], world["v6_stage"] = true, true, true, true, true
	content.Document, content.Hash, content.World = document, hash(data), world
	content.WorldHash = store.Hash(Canonical(world))
	content.V3 = &VersionBinding{Protocol: 6, Operation: "dc_ruins.v6", Content: "dc_content.v6.p5.2", World: "dc_rooms.v6.p5.2", ParametersHash: ph}
	content.Rules = content.rules()
	content.Rules.Version = "dc_ruins.v6.p5.2"
	content.FrameSchema, _, err = read("../contracts/frame-v6-p5.schema.json")
	if err != nil {
		return nil, err
	}
	content.OpenAPI, _, err = read("../contracts/openapi-v6-p5-runtime.json")
	if err != nil {
		return nil, err
	}
	return content, content.Rules.Validate()
}
func validateV6Frame(frame Frame) error {
	validated := frame
	if frame.Summary != nil {
		s := *frame.Summary
		s.Eligible = false
		validated.Summary = &s
	}
	if err := validateP3Frame(validated, 6); err != nil {
		return err
	}
	// Reuse v5's summon-origin and contribution filtering with the protocol identity translated only for validation.
	validated.Schema = 5
	return validateV5Frame(validated, true)
}
