package main

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"strings"

	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/v3"
)

type plannedParameters struct {
	SpecificationVersion string `json:"specification_version"`
	Status               string `json:"status"`
	Published            bool   `json:"published"`
	Versions             struct {
		Operation string `json:"operation"`
		Content   string `json:"content"`
		World     string `json:"world"`
		Trial     string `json:"trial"`
		Battle    int    `json:"battle_protocol"`
		Control   int    `json:"control_schema"`
	} `json:"versions"`
	Clocks struct {
		TargetMin int64 `json:"target_duration_min_ms"`
		TargetMax int64 `json:"target_duration_max_ms"`
		Active    int64 `json:"active_limit_ms"`
		Wall      int64 `json:"wall_limit_ms"`
		Choice    int64 `json:"choice_timeout_game_ms"`
		Wait      int64 `json:"technical_wait_after_wall_ms"`
		Abort     int64 `json:"technical_abort_after_wall_ms"`
		Reconnect int64 `json:"reconnect_grace_wall_ms"`
	} `json:"clocks"`
	Loadouts []json.RawMessage `json:"loadouts"`
	Growth   []json.RawMessage `json:"growth"`
	Rooms    []json.RawMessage `json:"rooms"`
	Tasks    []json.RawMessage `json:"tasks"`
}

func main() {
	root := flag.String("root", ".", "Demo repository root")
	flag.Parse()
	parametersPath := filepath.Join(*root, "docs", "game-demo", "contracts", "planned-v3", "parameters.json")
	openapiPath := filepath.Join(*root, "docs", "game-demo", "contracts", "planned-v3", "openapi.json")
	manifestPath := filepath.Join(*root, "client", "unity-demo", "Packages", "manifest.json")
	lockPath := filepath.Join(*root, "client", "unity-demo", "Packages", "packages-lock.json")
	projectSettingsPath := filepath.Join(*root, "client", "unity-demo", "ProjectSettings", "ProjectSettings.asset")
	migrationPath := filepath.Join(*root, "backend", "internal", "database", "migrations", "day44_dc_v3_p0_clock.sql")
	parametersData, err := os.ReadFile(parametersPath)
	if err != nil {
		fail(err)
	}
	var parameters plannedParameters
	if _, err := gameplay.DecodeJSON(parametersData); err != nil {
		fail(err)
	}
	if err := json.Unmarshal(parametersData, &parameters); err != nil {
		fail(err)
	}
	if parameters.SpecificationVersion != "2.0" || parameters.Status != "planned_not_loaded" || parameters.Published || parameters.Versions.Operation != "dc_ruins.v3" || parameters.Versions.Battle != 3 {
		fail(fmt.Errorf("planned-v3 version/status guard rejected"))
	}
	if parameters.Versions.Content != v3.ContentVersion || parameters.Versions.World != v3.WorldVersion || parameters.Versions.Trial != v3.TrialVersion || parameters.Versions.Control != 2 {
		fail(fmt.Errorf("planned-v3 content/world/trial/control version mismatch"))
	}
	if parameters.Clocks.TargetMin <= 0 || parameters.Clocks.TargetMin >= parameters.Clocks.TargetMax || parameters.Clocks.TargetMax >= parameters.Clocks.Active || parameters.Clocks.Active >= parameters.Clocks.Wall || parameters.Clocks.Choice <= 0 {
		fail(fmt.Errorf("planned-v3 clock ordering rejected"))
	}
	if parameters.Clocks.Wait <= 0 || parameters.Clocks.Abort <= parameters.Clocks.Wait || parameters.Clocks.Reconnect <= 0 || parameters.Clocks.Choice > parameters.Clocks.Active {
		fail(fmt.Errorf("planned-v3 recovery/choice timeout bounds rejected"))
	}
	if len(parameters.Loadouts) != 2 || len(parameters.Growth) != 10 || len(parameters.Rooms) != 8 || len(parameters.Tasks) != 6 {
		fail(fmt.Errorf("planned-v3 cardinality rejected"))
	}
	openapiData, err := os.ReadFile(openapiPath)
	if err != nil {
		fail(err)
	}
	var openapi map[string]any
	if err := json.Unmarshal(openapiData, &openapi); err != nil {
		fail(err)
	}
	if openapi["x-design-status"] != "planned_not_registered" {
		fail(fmt.Errorf("planned-v3 OpenAPI is registered unexpectedly"))
	}
	manifestData, err := os.ReadFile(manifestPath)
	if err != nil {
		fail(err)
	}
	var manifest struct {
		Dependencies map[string]string `json:"dependencies"`
	}
	if err := json.Unmarshal(manifestData, &manifest); err != nil {
		fail(err)
	}
	if manifest.Dependencies["com.unity.inputsystem"] != "1.19.0" {
		fail(fmt.Errorf("Unity Input System 1.19.0 is not locked in manifest"))
	}
	lockData, err := os.ReadFile(lockPath)
	if err != nil {
		fail(err)
	}
	var locked struct {
		Dependencies map[string]struct {
			Version string `json:"version"`
		} `json:"dependencies"`
	}
	if err := json.Unmarshal(lockData, &locked); err != nil {
		fail(err)
	}
	for name, version := range map[string]string{"com.unity.inputsystem": "1.19.0", "com.unity.netcode.gameobjects": "2.13.3", "com.unity.transport": "2.6.0"} {
		if manifest.Dependencies[name] != version || locked.Dependencies[name].Version != version {
			fail(fmt.Errorf("Unity manifest/lock version mismatch for %s", name))
		}
	}
	projectSettingsData, err := os.ReadFile(projectSettingsPath)
	if err != nil {
		fail(err)
	}
	if !regexp.MustCompile(`(?m)^\s*activeInputHandler:\s*2\s*$`).Match(projectSettingsData) {
		fail(fmt.Errorf("Unity Active Input Handling must be Both while v2 code uses the legacy API"))
	}
	migrationData, err := os.ReadFile(migrationPath)
	if err != nil {
		fail(err)
	}
	migrationText := string(migrationData)
	for _, table := range []string{"pve_dc_v3_run_clocks", "pve_dc_v3_pause_intervals"} {
		if !strings.Contains(migrationText, "CREATE TABLE IF NOT EXISTS "+table) {
			fail(fmt.Errorf("v3 P0 migration is missing idempotent table %s", table))
		}
	}
	runtime, err := gameplay.LoadContent(filepath.Join(*root, "docs", "game-demo", "config", "demo-v3-p0.json"))
	if err != nil {
		fail(err)
	}
	if runtime.ProtocolVersion() != 3 || runtime.Rules.Version != "dc_ruins.v3.p0" || len(runtime.Rules.Tasks) != 0 {
		fail(fmt.Errorf("P0 control runtime boundary rejected"))
	}
	digest := sha256.Sum256(parametersData)
	fmt.Printf("p0 planned-v3 checks passed operation=%s protocol=%d parameter_bytes=%d parameter_sha256=%s input_system=%s\n", parameters.Versions.Operation, parameters.Versions.Battle, len(parametersData), hex.EncodeToString(digest[:]), manifest.Dependencies["com.unity.inputsystem"])
}

func fail(err error) {
	fmt.Fprintln(os.Stderr, "p0 check failed:", err)
	os.Exit(1)
}
