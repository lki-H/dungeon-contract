package gameplay

import (
	"encoding/json"
	"path/filepath"
	"testing"
)

func TestQualityContentAndRoomValidation(test *testing.T) {
	content, err := LoadContent(filepath.Join(filepath.Dir(contentPath()), "demo-v2.json"))
	if err != nil {
		test.Fatal(err)
	}
	if content.ProtocolVersion() != 2 || content.Rules.Version != "dc_ruins.v2" || content.Rules.Tasks[0].Objectives[0].Required != 64 || content.Rules.Tasks[1].Objectives[0].Required != 100 {
		test.Fatal("quality version or contract thresholds incorrect")
	}
	for _, edit := range []func(*Content){
		func(value *Content) {
			objects(value.World, "rooms")[1]["spawn"] = []any{json.Number("0"), json.Number("4")}
		},
		func(value *Content) {
			objects(value.World, "rooms")[2]["objective"] = []any{json.Number("6"), json.Number("3")}
		},
		func(value *Content) { objects(value.World, "doors")[1]["to"] = json.Number("5") },
		func(value *Content) { objects(value.World, "rooms")[5]["height"] = json.Number("12") },
	} {
		invalid, err := LoadContent(filepath.Join(filepath.Dir(contentPath()), "demo-v2.json"))
		if err != nil {
			test.Fatal(err)
		}
		edit(invalid)
		if invalid.validateWorld() == nil {
			test.Fatal("unplayable quality room accepted")
		}
	}
}
