package gameplay

import (
	"bytes"
	"os"
	"path/filepath"

	"game-realtime-gm/backend/internal/pve/store"
)

func (service *Service) P3() bool {
	return service.Content.Rules.Version == "dc_ruins.v4.p3.1" || service.P4()
}

func loadP3(path string) (*Content, error) {
	content, err := loadV3P0(filepath.Join(filepath.Dir(path), "demo-v4-p2-mp.json"))
	if err != nil {
		return nil, err
	}
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	value, err := DecodeJSON(data)
	if err != nil {
		return nil, err
	}
	schemaData, err := os.ReadFile(filepath.Join(filepath.Dir(path), "demo-v4-p3.schema.json"))
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
	content.Document = value.(map[string]any)
	content.Hash = store.Hash(bytes.ReplaceAll(data, []byte("\r\n"), []byte("\n")))
	content.Rules = content.rules()
	content.Rules.Version = "dc_ruins.v4.p3.1"
	content.World["p3_stage"] = true
	content.WorldHash = store.Hash(Canonical(content.World))
	frameData, err := os.ReadFile(filepath.Join(filepath.Dir(path), "..", "contracts", "frame-v4-p3.schema.json"))
	if err != nil {
		return nil, err
	}
	frame, err := DecodeJSON(frameData)
	if err != nil {
		return nil, err
	}
	content.FrameSchema = frame.(map[string]any)
	contractData, err := os.ReadFile(filepath.Join(filepath.Dir(path), "..", "contracts", "openapi-v4-p3-runtime.json"))
	if err != nil {
		return nil, err
	}
	contract, err := DecodeJSON(contractData)
	if err != nil {
		return nil, err
	}
	content.OpenAPI = contract.(map[string]any)
	return content, content.Rules.Validate()
}

func validateP3Frame(frame Frame, protocols ...int) error {
	protocol := 4
	if len(protocols) > 0 {
		protocol = protocols[0]
	}
	if frame.Schema != protocol || frame.Source != "unity" || frame.WallElapsed != frame.Elapsed+frame.ManualPause || len(frame.Facts) == 0 || (frame.Terminal == nil) != (frame.Summary == nil) {
		return store.Invalid
	}
	for _, fact := range frame.Facts {
		if fact.EventGameMS != frame.Elapsed {
			return store.Invalid
		}
		if fact.Kind == "phase" {
			if fact.Actor != nil || fact.Action != nil || fact.Amount != 0 || len(fact.Contributors) != 0 {
				return store.Invalid
			}
		} else if fact.Actor == nil || fact.LifeGeneration < 1 {
			return store.Invalid
		}
	}
	if frame.Summary != nil && frame.Summary.Eligible {
		return store.Forbidden
	}
	if frame.Terminal != nil {
		switch frame.Terminal.Outcome {
		case "success":
			if frame.Terminal.Reason != "objectives_complete" {
				return store.Invalid
			}
		case "failed":
			if frame.Terminal.Reason != "team_wipe" && frame.Terminal.Reason != "timeout" {
				return store.Invalid
			}
		case "aborted":
			if frame.Terminal.Reason != "team_abandoned" && frame.Terminal.Reason != "technical_failure" {
				return store.Invalid
			}
		default:
			return store.Invalid
		}
	}
	return nil
}
