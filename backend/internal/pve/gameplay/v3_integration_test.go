package gameplay_test

import (
	"bytes"
	"encoding/json"
	"errors"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/store"
)

func TestV3P0RealAdmissionFramesAndVersionIsolation(test *testing.T) {
	test.Setenv("DC_V3_P0_TEST", "1")
	fixture := setup(test, 1)
	fixture.create(test, "")
	if fixture.manifest.Versions == nil || fixture.manifest.Versions.Protocol != 3 {
		test.Fatal("real allocation did not freeze protocol3")
	}
	valid := gameplay.Admission{Versions: fixture.game.Content.V3, Hash: fixture.game.Content.Hash, Player: fixture.players[0], RunID: fixture.state.ID, Operation: uuid()}
	legacy := valid
	legacy.Versions = nil
	if _, err := fixture.game.Issue(fixture.ctx, legacy); !errors.Is(err, store.Forbidden) {
		test.Fatal("legacy client admission accepted", err)
	}
	wrong := *valid.Versions
	wrong.World = "dc_rooms.v2"
	legacy.Versions = &wrong
	if _, err := fixture.game.Issue(fixture.ctx, legacy); !errors.Is(err, store.Forbidden) {
		test.Fatal("world mismatch accepted", err)
	}
	admission, err := fixture.game.Issue(fixture.ctx, valid)
	if err != nil {
		test.Fatal(err)
	}
	validateResponse := func(path string, value any) {
		operation := fixture.game.Content.OpenAPI["paths"].(map[string]any)[path].(map[string]any)["post"].(map[string]any)
		schema := operation["responses"].(map[string]any)["200"].(map[string]any)["content"].(map[string]any)["application/json"].(map[string]any)["schema"].(map[string]any)
		if reference, ok := schema["$ref"].(string); ok {
			schema = fixture.game.Content.OpenAPI["components"].(map[string]any)["schemas"].(map[string]any)[strings.TrimPrefix(reference, "#/components/schemas/")].(map[string]any)
		}
		decoded, err := gameplay.DecodeJSON(store.JSON(value))
		if err != nil || gameplay.ValidateSchema(decoded, schema) != nil {
			test.Fatal("runtime response contract mismatch", path, err)
		}
	}
	validateResponse("/api/v2/runs/{run_id}/admissions", admission)
	validateResponse("/internal/dc/v1/instances/{instance_id}/claim", map[string]any{"assignment": fixture.manifest})
	consume := gameplay.ConsumeRequest{Ticket: admission.Ticket, Nonce: uuid(), Instance: "dc_local_01", Boot: fixture.manifest.BootID, Versions: fixture.game.Content.V3, Hash: fixture.game.Content.Hash}
	badConsume := consume
	badConsume.Versions = nil
	if _, err := fixture.game.Consume(fixture.ctx, badConsume); !errors.Is(err, store.Forbidden) {
		test.Fatal("legacy service consumed v3 ticket", err)
	}
	badConsume = consume
	badConsume.Hash = "wrong"
	if _, err := fixture.game.Consume(fixture.ctx, badConsume); !errors.Is(err, store.Forbidden) {
		test.Fatal("wrong hash consumed v3 ticket", err)
	}
	identity, err := fixture.game.Consume(fixture.ctx, consume)
	if err != nil {
		test.Fatal(err)
	}
	validateResponse("/internal/dc/v1/admissions/consume", identity)
	if _, err := fixture.game.Consume(fixture.ctx, consume); err != nil {
		test.Fatal("idempotent consume failed", err)
	}
	consume.Nonce = uuid()
	if _, err := fixture.game.Consume(fixture.ctx, consume); !errors.Is(err, store.Conflict) {
		test.Fatal("ticket replay accepted", err)
	}
	fixture.start(test)
	frame := map[string]any{"schema_version": 3, "source": "unity", "run_id": fixture.state.ID, "instance_id": "dc_local_01", "boot_id": fixture.manifest.BootID, "assignment_id": fixture.manifest.AssignmentID, "source_generation": fixture.manifest.Generation, "sequence": 1, "frame_id": uuid(), "world_frame_id": 1, "elapsed_ms": 0, "wall_elapsed_ms": 0, "manual_pause_ms": 0, "control_version": 0, "facts": []any{map[string]any{"event_id": uuid(), "kind": "phase", "actor_player_id": nil, "target_id": "p0_active", "action_id": nil, "effective_amount": 0, "life_generation": 0, "downed_generation": 0, "event_game_ms": 0, "contributors": []string{}, "proof": nil}}, "terminal": nil, "summary": nil}
	encodeFrame := func() []byte {
		delete(frame, "fingerprint")
		value, err := gameplay.DecodeJSON(store.JSON(frame))
		if err != nil {
			test.Fatal(err)
		}
		frame["fingerprint"] = store.Hash(gameplay.Canonical(value))
		return store.JSON(frame)
	}
	data := encodeFrame()
	receipt, err := fixture.game.Frame(fixture.ctx, data)
	if err != nil || receipt.Applied != 1 {
		test.Fatal("protocol3 frame not confirmed", err)
	}
	if _, err := fixture.game.Frame(fixture.ctx, data); err != nil {
		test.Fatal("frame idempotency failed", err)
	}
	var protocol int
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT protocol_version FROM pve_dc_v3_run_clocks WHERE run_id=?", fixture.state.ID).Scan(&protocol); err != nil || protocol != 3 {
		test.Fatal("clock fact missing", err)
	}
	frame["schema_version"] = 2
	if _, err := fixture.game.ParseFrame(encodeFrame()); err == nil {
		test.Fatal("v2 frame accepted in v3 allocation")
	}
	frame["schema_version"] = 3
	frame["manual_pause_ms"] = 1
	if _, err := fixture.game.ParseFrame(encodeFrame()); err == nil {
		test.Fatal("unconfirmed pause accepted")
	}
	frame["manual_pause_ms"] = 0
	facts := frame["facts"].([]any)
	fact := facts[0].(map[string]any)
	fact["kind"] = "damage"
	if _, err := fixture.game.ParseFrame(encodeFrame()); err == nil {
		test.Fatal("unimplemented gameplay fact accepted")
	}
	legacyContent, err := gameplay.LoadContent(filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v2.json"))
	if err != nil {
		test.Fatal(err)
	}
	if _, err := (&gameplay.Service{Content: legacyContent}).ParseFrame(data); err == nil {
		test.Fatal("v3 frame accepted by v2 service")
	}
	if err := fixture.game.Register(fixture.ctx, gameplay.InstanceRequest{InstanceID: "dc_local_01", BootID: uuid(), ProtocolVersion: 2, ConfigVersions: []gameplay.ConfigVersion{{ID: "dc_ruins.v2", Hash: legacyContent.Hash}}}); err == nil {
		test.Fatal("old server took v3 instance")
	}
	frame["schema_version"] = 3
	fact["kind"] = "phase"
	frame["elapsed_ms"] = int64(1800001)
	frame["wall_elapsed_ms"] = int64(1800001)
	if _, err := fixture.game.ParseFrame(encodeFrame()); err == nil {
		test.Fatal("active time limit accepted")
	}
	frame["elapsed_ms"] = 0
	frame["wall_elapsed_ms"] = 1
	if _, err := fixture.game.ParseFrame(encodeFrame()); err == nil {
		test.Fatal("clock equation mismatch accepted")
	}
	frame["wall_elapsed_ms"] = 0
	frame["source_generation"] = fixture.manifest.Generation + 1
	if _, err := fixture.game.Frame(fixture.ctx, encodeFrame()); !errors.Is(err, store.Forbidden) {
		test.Fatal("stale source accepted", err)
	}
	fixture.clock = fixture.clock.Add(17 * time.Second)
	if err := fixture.game.Tick(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.EndReason != "system_abort" {
		test.Fatal("P0 control rule was excluded from lease maintenance", err)
	}
}

func TestV3P0HTTPStrictBoundary(test *testing.T) {
	test.Setenv("DC_V3_P0_TEST", "1")
	fixture := setup(test, 1)
	identity := uuid() + uuid()
	handler := fixture.game.Handler(identity)
	valid := map[string]any{"instance_id": "dc_local_01", "boot_id": fixture.boot(test), "protocol_version": 3, "versions": fixture.game.Content.V3, "config_versions": []any{map[string]any{"definition_id": "dc_ruins.v3.p0", "hash": fixture.game.Content.Hash}}}
	call := func(data []byte, authorized bool) int {
		request := httptest.NewRequest("POST", "/internal/dc/v1/instances/register", bytes.NewReader(data))
		if authorized {
			request.Header.Set("Authorization", "Bearer "+identity)
			request.Header.Set("X-DC-Service-ID", fixture.game.ServiceID)
		}
		response := httptest.NewRecorder()
		handler.ServeHTTP(response, request)
		return response.Code
	}
	if code := call(store.JSON(valid), true); code != 200 {
		test.Fatal("v3 runtime contract rejected valid request", code)
	}
	if code := call(store.JSON(valid), false); code != 401 {
		test.Fatal("untrusted service accepted", code)
	}
	for _, data := range [][]byte{[]byte(`{"boot_id":"a","boot_id":"b"}`), []byte(strings.Repeat(" ", 65537))} {
		if code := call(data, true); code != 400 {
			test.Fatal("duplicate key/oversized request accepted", code)
		}
	}
	valid["unknown"] = true
	if code := call(store.JSON(valid), true); code != 400 {
		test.Fatal("unknown field accepted", code)
	}
	delete(valid, "unknown")
	valid["protocol_version"] = 2
	if code := call(store.JSON(valid), true); code != 400 {
		test.Fatal("old protocol accepted by HTTP dispatcher", code)
	}
}

func TestV3P0ContentAndLegacyDispatch(test *testing.T) {
	root := filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config")
	for _, scenario := range []struct {
		name     string
		protocol int
	}{{"demo-v1.json", 1}, {"demo-v2.json", 2}, {"demo-v3-p0.json", 3}} {
		content, err := gameplay.LoadContent(filepath.Join(root, scenario.name))
		if err != nil {
			test.Fatal(scenario.name, err)
		}
		if content.ProtocolVersion() != scenario.protocol {
			test.Fatal("wrong content dispatcher")
		}
	}
	data, err := os.ReadFile(filepath.Join(root, "demo-v3-p0.json"))
	if err != nil {
		test.Fatal(err)
	}
	var doc map[string]any
	if err := json.Unmarshal(data, &doc); err != nil {
		test.Fatal(err)
	}
	doc["parameters_sha256"] = strings.Repeat("0", 64)
	temporary := test.TempDir()
	for _, relative := range []string{"config/demo-v3-p0.schema.json", "contracts/planned-v3/parameters.json", "contracts/planned-v3/parameters.schema.json", "contracts/frame-v3-p0.schema.json", "contracts/openapi-v3-p0-runtime.json"} {
		original, err := os.ReadFile(filepath.Join(root, "..", relative))
		if err != nil {
			test.Fatal(err)
		}
		target := filepath.Join(temporary, relative)
		if err := os.MkdirAll(filepath.Dir(target), 0700); err != nil {
			test.Fatal(err)
		}
		if err := os.WriteFile(target, original, 0600); err != nil {
			test.Fatal(err)
		}
	}
	path := filepath.Join(temporary, "config", "demo-v3-p0.json")
	if err := os.WriteFile(path, store.JSON(doc), 0600); err != nil {
		test.Fatal(err)
	}
	if _, err := gameplay.LoadContent(path); err == nil || !strings.Contains(err.Error(), "parameter hash mismatch") {
		test.Fatal("wrong parameter hash was not the rejection reason", err)
	}
	doc["parameters_sha256"] = "6a0afbb8ef6c27283a2ec8189bb11c5e0bf35d6f228d9f2f82d96ade0d81bc09"
	authority := doc["authority"].(map[string]any)
	for _, scenario := range []struct {
		key   string
		value int64
	}{{"max_pending_frames", 0}, {"max_frame_bytes", 65537}, {"max_key_frames", 1799}} {
		prior := authority[scenario.key]
		authority[scenario.key] = scenario.value
		if err := os.WriteFile(path, store.JSON(doc), 0600); err != nil {
			test.Fatal(err)
		}
		if _, err := gameplay.LoadContent(path); err == nil {
			test.Fatal("invalid P0 resource budget accepted", scenario.key)
		}
		authority[scenario.key] = prior
	}
}

func TestV3BindingCannotFallBackToLegacy(test *testing.T) {
	root := filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config")
	current, err := gameplay.LoadContent(filepath.Join(root, "demo-v3-p0.json"))
	if err != nil {
		test.Fatal(err)
	}
	for _, name := range []string{"demo-v1.json", "demo-v2.json"} {
		legacy, err := gameplay.LoadContent(filepath.Join(root, name))
		if err != nil {
			test.Fatal(err)
		}
		service := &gameplay.Service{Content: legacy}
		if _, err := service.Issue(nil, gameplay.Admission{Versions: current.V3}); !errors.Is(err, store.Forbidden) {
			test.Fatal("v3 client silently fell back to a legacy admission", name, err)
		}
		if _, err := service.Consume(nil, gameplay.ConsumeRequest{Versions: current.V3}); !errors.Is(err, store.Forbidden) {
			test.Fatal("v3 service silently fell back to a legacy admission", name, err)
		}
		if err := service.Heartbeat(nil, gameplay.InstanceRequest{Versions: current.V3}); !errors.Is(err, store.Forbidden) {
			test.Fatal("v3 heartbeat silently fell back to a legacy instance", name, err)
		}
	}
}
