package main

import (
	"bytes"
	"context"
	"crypto/rand"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"time"

	"game-realtime-gm/backend/internal/config"
	"game-realtime-gm/backend/internal/database"
	"game-realtime-gm/backend/internal/dctestnet"
	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/store"
	"github.com/gorilla/websocket"
)

type client struct {
	username, password, token string
	player                    int64
	socket                    *websocket.Conn
	proposal                  string
}
type harness struct {
	ctx               context.Context
	http              *http.Client
	processes         []*exec.Cmd
	clients           []*client
	output            string
	runID             string
	udpRelays         []*dctestnet.Relay
	udpProfile        string
	memoryObserver    *exec.Cmd
	memoryWatchCancel context.CancelFunc
	processRoster     []map[string]any
	trial             bool
	renderClients     int
}

func main() {
	if err := execute(); err != nil {
		fmt.Fprintln(os.Stderr, "Demo process verification failed:", err)
		os.Exit(1)
	}
}
func execute() (verificationError error) {
	v8Stage := flag.Bool("v8", false, "Protocol8 2.5 single-client functional verification")
	v7Stage := flag.Bool("v7", false, "Protocol7 2.4 engineering profile")
	v6Stage := flag.Bool("v6", false, "Protocol6 2.3 engineering profile")
	v5Stage := flag.Bool("v5", false, "Protocol5 P2.2-P4 graybox implementation")
	p5Stage := flag.Bool("p5", false, "P5 complete presentation and released trial profile")
	p5LobbyFlow := flag.Bool("p5-lobby", false, "P5 Unity login, two admissions, public leave and same-service rotation")
	trialMode := flag.Bool("trial", false, "Engineering trial chain; always ineligible for normal friend comparison")
	uniformLoadout := flag.Bool("uniform-loadout", false, "Use the selected loadout for every engineering player")
	p4Stage := flag.Bool("p4", false, "P4 personal growth and finite content stage")
	p3Stage := flag.Bool("p3", false, "P3 authoritative cooperation and business stage")
	windowWidth := flag.Int("window-width", 1536, "P3 human window width chosen within display working area")
	windowHeight := flag.Int("window-height", 864, "P3 human window height")
	contract := flag.String("contract", "", "optional P3 long-term contract key")
	playerCount := flag.Int("players", 1, "P2 stage real client count: 1, 2 or 4")
	renderClients := flag.Int("render-clients", -1, "V7 automated full-flow rendering clients; -1 uses visual default, 0 is headless")
	fullFlow := flag.Bool("full-flow", false, "P2 single-player input-only complete success or failure run")
	combatPressure := flag.Bool("combat-pressure", false, "P2 multi-client input-only combat and renderer metrics")
	udpProfile := flag.String("udp-profile", "direct", "v6 input-only loopback UDP conditioner: direct, mild, adverse")
	roomsStage := flag.Bool("rooms21", false, "verify protocol4 P2.1 room camera projectile graybox")
	weaponStage := flag.Bool("weapons2", false, "verify protocol4 single weapon and skill stage")
	syncStage := flag.Bool("sync1", false, "verify protocol4 sync and real pause foundation")
	p1V3 := flag.Bool("p1v3", false, "verify a real single-player protocol3 P1 world")
	human := flag.Bool("human", false, "open a P1 single-player session until the player exits")
	v3P0 := flag.Bool("v3", false, "verify real protocol3 P0 dispatch and UDP admission")
	p1 := flag.Bool("p1", false, "verify real single-player graybox combat")
	scheme := flag.String("loadout", "ranger", "ranger or runesmith")
	visual := flag.Bool("visual", false, "render the P1 client for UI inspection")
	scenario := flag.String("scenario", "success", "P1 success or wipe input-only scenario")
	lifecycle := flag.Bool("lifecycle", false, "verify same-client replay, reconnect and leave using public commands")
	lobbyExpiry := flag.Bool("lobby-expiry", false, "verify Unity lobby rejection, recovery, requeue and real admission")
	flag.Parse()
	if *v8Stage {
		*v7Stage = true
	}
	if *renderClients < -1 || *renderClients > *playerCount || *renderClients >= 0 && (!*v7Stage || !*fullFlow || *human) {
		return errors.New("render-clients requires an automated v7 full-flow and count within party size")
	}
	if *renderClients < 0 {
		*renderClients = 0
		if *visual {
			*renderClients = *playerCount
		}
	}
	if *v7Stage {
		*v6Stage = true
	}
	if *v6Stage {
		*p5Stage = true
	}
	if *p5LobbyFlow {
		*p5Stage = true
		*lobbyExpiry = true
		*visual = true
		if *playerCount != 1 || *fullFlow || *trialMode {
			return errors.New("P5 lobby verification is a bounded single-client normal-mode workflow")
		}
	}
	if *p5Stage {
		*v5Stage = true
		if err := os.Setenv("DC_ENGINEERING_RUN", "1"); err != nil {
			return err
		}
	}
	if *trialMode && !*p5Stage {
		return errors.New("trial verification requires the independent P5 profile")
	}
	if *v5Stage {
		*p4Stage = true
	}
	if *windowWidth < 320 || *windowWidth > 7680 || *windowHeight < 180 || *windowHeight > 4320 {
		return errors.New("invalid P3 window dimensions")
	}
	if *p4Stage {
		*p3Stage = true
	}
	if *p3Stage {
		*roomsStage = true
	}
	if *playerCount != 1 && *playerCount != 2 && *playerCount != 4 {
		return errors.New("P2 player count must be 1, 2 or 4")
	}
	if *playerCount > 1 {
		*roomsStage = true
	}
	if *fullFlow && !*roomsStage {
		return errors.New("Full flow requires P2 stage")
	}
	if *combatPressure && *playerCount == 1 {
		return errors.New("Combat pressure requires two or four P2 clients")
	}
	if *roomsStage {
		*weaponStage = true
	}
	if *weaponStage {
		*syncStage = true
	}
	if *syncStage {
		*p1V3 = true
	}
	if *p1V3 {
		*v3P0 = true
	}
	if *human && !*p1V3 {
		return errors.New("human entry requires P1 v3")
	}
	if *v3P0 && *p1 {
		return errors.New("v3 P0 excludes P1 combat")
	}
	if *lobbyExpiry && !*p5LobbyFlow && (!*p1 || *lifecycle) {
		return errors.New("lobby expiry requires P1 and excludes lifecycle")
	}
	if *scheme != "ranger" && *scheme != "runesmith" {
		return errors.New("invalid loadout")
	}
	if *scenario != "success" && *scenario != "wipe" && !(*p3Stage && *scenario == "rescue") {
		return errors.New("invalid P1 scenario")
	}
	if *scenario == "rescue" && (*fullFlow || *combatPressure) {
		return errors.New("rescue is a bounded probe; omit full-flow and combat-pressure")
	}
	if *udpProfile != "direct" {
		if !*v6Stage || *human || *p5LobbyFlow || !(*fullFlow || *combatPressure) {
			return errors.New("UDP conditioning requires an automated v6 full-flow or combat-pressure scenario")
		}
		if _, err := dctestnet.Named(*udpProfile); err != nil {
			return err
		}
	}
	configuration := config.Load()
	if err := config.ValidateDemo(configuration); err != nil {
		return err
	}
	duration := 2 * time.Minute
	if *p3Stage {
		duration = 10 * time.Minute
	}
	if *human {
		duration = 65 * time.Minute
	}
	if *p1 {
		duration = 23 * time.Minute
	}
	if *fullFlow {
		duration = 35 * time.Minute
	}
	if *combatPressure {
		duration = 3 * time.Minute
	}
	ctx, cancel := context.WithTimeout(context.Background(), duration)
	defer cancel()
	stage := "p0"
	if *v3P0 {
		stage = "p0-v3"
	}
	if *p1V3 {
		stage = "p1-v3-" + *scheme
	}
	if *syncStage {
		stage = "s1-v4-" + *scheme
		if *weaponStage {
			stage = "s2-v4-" + *scheme
		}
	}
	if *p1 {
		stage = "p1-" + *scheme
	}
	if *roomsStage {
		stage = "p2-1-v4-" + *scheme
	}
	if *playerCount > 1 {
		stage = fmt.Sprintf("p2-multiplayer-%d", *playerCount)
	}
	if *p3Stage {
		stage = fmt.Sprintf("p3-%s-%d-%s", *scheme, *playerCount, *scenario)
	}
	if *p4Stage {
		stage = fmt.Sprintf("p4-%s-%d-%s", *scheme, *playerCount, *scenario)
	}
	if *v5Stage {
		stage = fmt.Sprintf("v5-%s-%d-%s", *scheme, *playerCount, *scenario)
		if *v6Stage {
			stage = fmt.Sprintf(modernRuntime()+"-%s-%d-%s", *scheme, *playerCount, *scenario)
		}
	}
	output := "D:/dev-cache/dungeon-contract/evidence/" + stage + "-process-" + time.Now().Format("20060102-150405")
	if err := os.MkdirAll(output, 0700); err != nil {
		return err
	}
	runner := &harness{ctx: ctx, http: &http.Client{Timeout: 3 * time.Second}, output: output, trial: *trialMode}
	runner.renderClients = *renderClients
	defer runner.cleanup()
	defer func() {
		if verificationError != nil {
			if _, guardErr := os.Stat(filepath.Join(output, "memory-stop.json")); guardErr == nil {
				verificationError = fmt.Errorf("owned verification stopped by system memory guard; see memory-stop.json and process-memory.json: %w", verificationError)
			}
			_ = os.WriteFile(filepath.Join(output, "failure.json"), store.JSON(map[string]any{"run_id": runner.runID, "reason": verificationError.Error(), "passed": false, "human": false}), 0600)
		}
	}()
	runner.udpProfile = *udpProfile
	defer func() {
		if err := runner.finishUDP(); err != nil && verificationError == nil {
			verificationError = err
			_ = os.Rename(filepath.Join(output, "summary.json"), filepath.Join(output, "gameplay-only-summary.json"))
		}
	}()
	controlExe := "D:/dev-cache/dungeon-contract/builds/dc-control.exe"
	if *v6Stage {
		if err := runner.startMemoryObserver(cancel); err != nil {
			return err
		}
		defer func() {
			if err := runner.finishMemoryObserver(); err != nil && verificationError == nil {
				verificationError = err
				_ = os.Rename(filepath.Join(output, "summary.json"), filepath.Join(output, "gameplay-only-summary.json"))
			}
		}()
	}
	serviceExe := "D:/dev-cache/dungeon-contract/builds/p0/GameService/GameService.exe"
	playerExe := "D:/dev-cache/dungeon-contract/builds/p0/DungeonContract/DungeonContract.exe"
	if *v3P0 {
		serviceExe = "D:/dev-cache/dungeon-contract/builds/p0-v3/GameService/GameService.exe"
		playerExe = "D:/dev-cache/dungeon-contract/builds/p0-v3/DungeonContract/DungeonContract.exe"
	}
	if *p1V3 {
		serviceExe = "D:/dev-cache/dungeon-contract/builds/p1-v3/GameService/GameService.exe"
		playerExe = "D:/dev-cache/dungeon-contract/builds/p1-v3/DungeonContract/DungeonContract.exe"
	}
	if *syncStage {
		serviceExe = "D:/dev-cache/dungeon-contract/builds/s1-v4/GameService/GameService.exe"
		playerExe = "D:/dev-cache/dungeon-contract/builds/s1-v4/DungeonContract/DungeonContract.exe"
	}
	if *weaponStage {
		serviceExe = "D:/dev-cache/dungeon-contract/builds/s2-v4/GameService/GameService.exe"
		playerExe = "D:/dev-cache/dungeon-contract/builds/s2-v4/DungeonContract/DungeonContract.exe"
	}
	if *p1 {
		serviceExe = "D:/dev-cache/dungeon-contract/builds/p1/GameService/GameService.exe"
		playerExe = "D:/dev-cache/dungeon-contract/builds/p1/DungeonContract/DungeonContract.exe"
	}
	if *roomsStage {
		serviceExe = "D:/dev-cache/dungeon-contract/builds/p2-1-v4/GameService/GameService.exe"
		playerExe = "D:/dev-cache/dungeon-contract/builds/p2-1-v4/DungeonContract/DungeonContract.exe"
	}
	if *p3Stage {
		serviceExe = "D:/dev-cache/dungeon-contract/builds/p3-v4/GameService/GameService.exe"
		playerExe = "D:/dev-cache/dungeon-contract/builds/p3-v4/DungeonContract/DungeonContract.exe"
	}
	if *p4Stage {
		serviceExe = "D:/dev-cache/dungeon-contract/builds/p4-v4/GameService/GameService.exe"
		playerExe = "D:/dev-cache/dungeon-contract/builds/p4-v4/DungeonContract/DungeonContract.exe"
	}
	if *v5Stage {
		controlExe = "D:/dev-cache/dungeon-contract/builds/v5-p4/dc-control.exe"
		serviceExe = "D:/dev-cache/dungeon-contract/builds/v5-p4/GameService/GameService.exe"
		playerExe = "D:/dev-cache/dungeon-contract/builds/v5-p4/DungeonContract/DungeonContract.exe"
	}
	if *p5Stage {
		controlExe = "D:/dev-cache/dungeon-contract/builds/v5-p5/dc-control.exe"
		serviceExe = "D:/dev-cache/dungeon-contract/builds/v5-p5/GameService/GameService.exe"
		playerExe = "D:/dev-cache/dungeon-contract/builds/v5-p5/DungeonContract/DungeonContract.exe"
	}
	if *v6Stage {
		controlExe = "D:/dev-cache/dungeon-contract/builds/" + modernRuntime() + "-p5/dc-control.exe"
		serviceExe = "D:/dev-cache/dungeon-contract/builds/" + modernRuntime() + "-p5/GameService/GameService.exe"
		playerExe = "D:/dev-cache/dungeon-contract/builds/" + modernRuntime() + "-p5/DungeonContract/DungeonContract.exe"
	}
	if *v5Stage {
		fingerprints := []map[string]any{}
		for _, entry := range []struct{ role, path string }{{"control", controlExe}, {"authority_managed", filepath.Join(filepath.Dir(serviceExe), "GameService_Data", "Managed", "Assembly-CSharp.dll")}, {"client_managed", filepath.Join(filepath.Dir(playerExe), "DungeonContract_Data", "Managed", "Assembly-CSharp.dll")}} {
			data, err := os.ReadFile(entry.path)
			if err != nil {
				return errors.New("v5 runtime binary fingerprint unavailable")
			}
			fingerprints = append(fingerprints, map[string]any{"role": entry.role, "path": entry.path, "bytes": len(data), "sha256": store.Hash(data)})
		}
		binaryProfile := "dc_ruins.v5.p4.1"
		if *p5Stage {
			binaryProfile = "dc_ruins.v5.p5.1"
			if *v6Stage {
				binaryProfile = modernProfile()
			}
		}
		if err := os.WriteFile(filepath.Join(output, "runtime-binaries.json"), store.JSON(map[string]any{"profile": binaryProfile, "recorded_before_launch": time.Now().UTC(), "files": fingerprints}), 0600); err != nil {
			return err
		}
	}
	if err := runner.launch(controlExe, "control", nil, true); err != nil {
		return err
	}
	controlReady := false
	for attempt := 0; attempt < 50; attempt++ {
		response, err := runner.http.Get("http://127.0.0.1:12880/ready")
		if err == nil {
			response.Body.Close()
			if response.StatusCode == http.StatusOK {
				controlReady = true
				break
			}
		}
		time.Sleep(200 * time.Millisecond)
	}
	if !controlReady {
		return errors.New("control service did not become ready")
	}
	battle := "false"
	if *p1 {
		battle = "true"
	}
	bootID := uuidString()
	serviceArguments := []string{"-dc-boot-id", bootID, "-dc-service-credential", "D:/dev-cache/dungeon-contract/private/service-token.txt", "-dc-config", os.Getenv("DC_CONFIG_PATH"), "-dc-battle", battle}
	if *p5Stage {
		serviceArguments = append(serviceArguments, "-dc-p5", "true")
	}
	if *v3P0 {
		runtime := "v3"
		if *syncStage {
			runtime = "v4"
			if *v5Stage {
				runtime = "v5"
				if *v6Stage {
					runtime = modernRuntime()
				}
			}
		}
		serviceArguments = append(serviceArguments, "-dc-runtime", runtime, "-dc-p0-evidence", filepath.Join(output, "server-v3.json"))
	}
	if *p1V3 {
		stage := "p1"
		if *weaponStage {
			stage = "s2"
		}
		if *roomsStage {
			stage = "p2"
		}
		if *playerCount > 1 {
			stage = "p2-mp"
		}
		if *p3Stage {
			stage = "p3"
		}
		if *p4Stage {
			stage = "p4"
		}
		serviceArguments = append(serviceArguments, "-dc-stage", stage)
	}
	if err := runner.launch(serviceExe, "server", serviceArguments); err != nil {
		return err
	}
	db, err := database.NewMySQLDB(ctx, configuration.Database)
	if err != nil {
		return err
	}
	defer db.Close()
	deadline := time.Now().Add(20 * time.Second)
	ready := false
	for time.Now().Before(deadline) {
		var status string
		err := db.QueryRowContext(ctx, "SELECT status FROM pve_dc_instances WHERE instance_id='dc_local_01' AND boot_id=? AND lease_until>UTC_TIMESTAMP(3)", bootID).Scan(&status)
		if err == nil && status == "idle" {
			ready = true
			break
		}
		time.Sleep(200 * time.Millisecond)
	}
	if !ready {
		return errors.New("standalone service did not register idle")
	}
	count := 4
	if *p1 || *p1V3 {
		count = 1
	}
	if *roomsStage {
		count = *playerCount
	}
	for index := 0; index < count; index++ {
		account := &client{username: "dcsmoke_" + rand.Text(), password: rand.Text() + rand.Text()}
		fresh := true
		localProfile := filepath.Join("D:/dev-cache/dungeon-contract/private", fmt.Sprintf("p3-local-player-%d.json", index))
		if *p3Stage && *human {
			account, fresh, err = loadP3LocalAccount(localProfile)
			if err != nil {
				return err
			}
		}
		if fresh {
			nickname := "P0 process"
			if *p3Stage && *human {
				nickname = "Dungeon Contract"
			}
			if _, err := runner.request("/api/register", "", map[string]any{"username": account.username, "password": account.password, "nickname": nickname}); err != nil {
				return err
			}
		}
		response, err := runner.request("/api/login", "", map[string]any{"username": account.username, "password": account.password})
		if err != nil {
			return err
		}
		var login struct {
			Data struct {
				Token  string `json:"token"`
				Player struct {
					ID int64 `json:"id"`
				} `json:"player"`
			} `json:"data"`
		}
		if err := json.Unmarshal(response, &login); err != nil {
			return err
		}
		account.token = login.Data.Token
		account.player = login.Data.Player.ID
		if *p3Stage && *human && fresh {
			if err := saveP3LocalAccount(localProfile, account); err != nil {
				return err
			}
		}
		header := http.Header{}
		header.Set("Authorization", "Bearer "+account.token)
		socket, _, err := websocket.DefaultDialer.DialContext(ctx, "ws://127.0.0.1:12880/ws", header)
		if err != nil {
			return errors.New("control WebSocket connection failed")
		}
		account.socket = socket
		runner.clients = append(runner.clients, account)
		fill := "public"
		loadout := map[bool]string{true: "ranger", false: "runesmith"}[index%2 == 0]
		if *p1 || *p1V3 {
			fill = "no_fill"
			loadout = *scheme
		}
		if *playerCount > 1 {
			fill = "public"
			loadout = map[bool]string{true: "ranger", false: "runesmith"}[index%2 == 0]
		}
		if *uniformLoadout {
			loadout = *scheme
		}
		if !*lobbyExpiry {
			ruleVersion := "dc_ruins.v2"
			if *v3P0 {
				ruleVersion = "dc_ruins.v3.p0"
			}
			if *p1V3 {
				ruleVersion = "dc_ruins.v3.p1"
				if *syncStage {
					ruleVersion = "dc_ruins.v4.s1"
					if *weaponStage {
						ruleVersion = "dc_ruins.v4.s2"
					}
					if *roomsStage {
						ruleVersion = "dc_ruins.v4.p2.1"
					}
					if *playerCount > 1 {
						ruleVersion = "dc_ruins.v4.p2.multiplayer"
					}
				}
			}
			selection := map[string]any{"loadout_id": loadout}
			if *p3Stage {
				ruleVersion = "dc_ruins.v4.p3.1"
				if *contract != "" {
					selection["task_key"] = *contract
					selection["task_version"] = "dc_ruins.v4.p3.1"
				}
			}
			if *p4Stage {
				ruleVersion = "dc_ruins.v4.p4.1"
			}
			if *v5Stage {
				ruleVersion = "dc_ruins.v5.p4.1"
				if *contract != "" {
					selection["task_version"] = "dc_tasks.v5.p4.1"
				}
			}
			if *p5Stage {
				ruleVersion = "dc_ruins.v5.p5.1"
				if *v6Stage {
					ruleVersion = modernProfile()
				}
			}
			plan := map[string]any{"operation": "dc_ruins", "difficulty": "standard", "rule_version": ruleVersion, "fill_policy": fill, "allow_partial": true}
			if *trialMode {
				plan["mode"] = "trial"
				plan["trial_id"] = "dc_ruins_trial"
				plan["trial_version"] = "trial.v5.1"
				if *v6Stage {
					plan["trial_version"] = modernTrial()
				}
			}
			if _, err := runner.command(account, "v2.match.enqueue", map[string]any{"plan": plan, "task": selection}); err != nil {
				return err
			}
		}
	}
	if !*lobbyExpiry {
		for _, account := range runner.clients {
			if account.proposal == "" {
				if err := runner.waitProposal(account); err != nil {
					return err
				}
			}
			data, err := runner.command(account, "v2.match.proposal_confirm", map[string]any{"proposal_id": account.proposal, "revision": 1})
			if err != nil {
				return err
			}
			var result struct {
				RunID string `json:"run_id"`
			}
			json.Unmarshal(data, &result)
			if result.RunID != "" {
				runner.runID = result.RunID
			}
		}
	}
	if runner.runID == "" && !*lobbyExpiry {
		return errors.New("candidate confirmations did not assign a run")
	}
	for index, account := range runner.clients {
		if *v6Stage && index > 0 {
			select {
			case <-runner.ctx.Done():
				return errors.New("verification stopped before the next owned client launch")
			case <-time.After(1200 * time.Millisecond):
			}
		}
		passwordFile := filepath.Join("D:/dev-cache/dungeon-contract/private", fmt.Sprintf("p0-client-%d.txt", index))
		if err := os.WriteFile(passwordFile, []byte(account.password), 0600); err != nil {
			return err
		}
		extra := []string{"-dc-user", account.username, "-dc-password-file", passwordFile}
		if !*lobbyExpiry {
			extra = append(extra, "-dc-run", runner.runID)
		}
		if *v3P0 {
			if *v6Stage {
				extra = append(extra, "-dc-v6-driver-evidence", filepath.Join(output, fmt.Sprintf("client-driver-%d.json", index)))
			}
			runtime := "v3"
			if *syncStage {
				runtime = "v4"
				if *v5Stage {
					runtime = "v5"
					if *v6Stage {
						runtime = modernRuntime()
					}
				}
			}
			extra = append(extra, "-dc-runtime", runtime, "-dc-p0-evidence", filepath.Join(output, fmt.Sprintf("client-%d-v3.json", index)))
			if *p5Stage {
				extra = append(extra, "-dc-p5", "true")
			}
			if *p5LobbyFlow {
				extra = append(extra, "-dc-p5-lobby-probe", "true", "-dc-p5-lobby-output", filepath.Join(output, "p5-lobby.json"))
			}
		}
		if *p1 {
			extra = append(extra, "-dc-graybox-autoplay", "true")
			if *lobbyExpiry {
				extra = append(extra, "-dc-lobby-confirm-check", "true", "-dc-lobby-output", filepath.Join(output, "client-lobby.json"), "-dc-loadout", *scheme)
			}
			if *lifecycle {
				extra = append(extra, "-dc-lifecycle-check", "true", "-dc-lifecycle-output", filepath.Join(output, "client-lifecycle.json"))
			}
			if *scenario == "wipe" {
				extra = append(extra, "-dc-defeat-autoplay", "true")
			}
			if *visual {
				extra = append(extra, "-dc-visual", "true", "-force-d3d11", "-dc-capture-dir", filepath.Join(output, "screenshots"), "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720")
			}
		}
		if *p1V3 {
			stage := "p1"
			if *weaponStage {
				stage = "s2"
			}
			if *roomsStage {
				stage = "p2"
			}
			if *playerCount > 1 {
				stage = "p2-mp"
			}
			if *p3Stage {
				stage = "p3"
			}
			if *p4Stage {
				stage = "p4"
			}
			extra = append(extra, "-dc-stage", stage, "-dc-p1-evidence", filepath.Join(output, p2WorldEvidenceName(index, count)))
			if *p4Stage {
				extra = append(extra, "-dc-p4-evidence", filepath.Join(output, fmt.Sprintf("client-p4-%d.json", index)))
			}
			if *human {
				width, height := "1280", "720"
				if *p3Stage {
					width, height = fmt.Sprint(*windowWidth), fmt.Sprint(*windowHeight)
				}
				extra = append(extra, "-dc-visual", "true", "-force-d3d11", "-screen-fullscreen", "0", "-screen-width", width, "-screen-height", height)
			} else {
				extra = append(extra, "-dc-p1-autoplay", "true")
			}
			if *fullFlow || *p3Stage && *scenario == "rescue" {
				metricsName := "client-full-metrics.json"
				if count > 1 {
					metricsName = fmt.Sprintf("client-full-metrics-%d.json", index)
				}
				extra = append(extra, "-dc-p2-autoplay", "true", "-dc-s1-metrics", filepath.Join(output, metricsName))
			}
			if *p3Stage && *scenario == "rescue" {
				extra = append(extra, "-dc-p3-rescue", "true")
			}
			if *fullFlow && *scenario == "wipe" {
				extra = append(extra, "-dc-p2-wipe", "true")
			}
			if *playerCount > 1 && !(*p3Stage && (*fullFlow || *scenario == "rescue")) {
				extra = append(extra, "-dc-p2-network-probe", "true", "-dc-p2-probe-slot", fmt.Sprint(index), "-dc-p2-probe-output", filepath.Join(output, fmt.Sprintf("client-p2-%d.json", index)))
			}
			if *combatPressure {
				extra = append(extra, "-dc-p2-combat-pressure", "true", "-dc-p2-probe-duration", "90", "-dc-s1-metrics", filepath.Join(output, fmt.Sprintf("client-%d-metrics.json", index)))
			}
			if *visual && !*human && index < *renderClients {
				extra = append(extra, "-dc-visual", "true", "-force-d3d11", "-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720")
			}
			if *p3Stage && *fullFlow && *visual && index < *renderClients {
				extra = append(extra, "-dc-p3-result-capture", filepath.Join(output, fmt.Sprintf("result-%d.png", index)))
			}
		}
		if *weaponStage && !*human && !*fullFlow && *playerCount == 1 && !*p5LobbyFlow {
			extra = append(extra, "-dc-s2-probe", "true", "-dc-s2-probe-output", filepath.Join(output, "client-s2-probe.json"))
		}
		if *syncStage && !*human && !*weaponStage {
			extra = append(extra, "-dc-s1-probe", "true", "-dc-s1-probe-output", filepath.Join(output, "client-s1-probe.json"), "-dc-s1-metrics", filepath.Join(output, "client-s1-metrics.json"))
		}
		if *udpProfile != "direct" {
			profile, _ := dctestnet.Named(*udpProfile)
			port := 12970 + index
			relay, err := dctestnet.New(runner.ctx, fmt.Sprintf("127.0.0.1:%d", port), "127.0.0.1:12777", profile, int64(20261017+index))
			if err != nil {
				return err
			}
			runner.udpRelays = append(runner.udpRelays, relay)
			extra = append(extra, "-dc-v6-test-udp-port", fmt.Sprint(port))
		}
		if err := runner.launch(playerExe, fmt.Sprintf("client-%d", index), extra); err != nil {
			return err
		}
	}
	if *p5LobbyFlow {
		return runner.verifyP5Lobby(db)
	}
	deadline = time.Now().Add(25 * time.Second)
	if *lobbyExpiry {
		deadline = time.Now().Add(45 * time.Second)
		for time.Now().Before(deadline) {
			var evidence struct {
				RunID    string `json:"run_id"`
				Expired  bool   `json:"expired_confirmation_rejected"`
				Requeued bool   `json:"requeue_succeeded"`
				World    bool   `json:"world_snapshot_received"`
			}
			data, err := os.ReadFile(filepath.Join(output, "client-lobby.json"))
			if err == nil && json.Unmarshal(data, &evidence) == nil && evidence.RunID != "" && evidence.Expired && evidence.Requeued && evidence.World {
				runner.runID = evidence.RunID
				break
			}
			time.Sleep(200 * time.Millisecond)
		}
		if runner.runID == "" {
			return errors.New("Unity lobby expiry recovery did not load the world")
		}
		deadline = time.Now().Add(10 * time.Second)
	}
	started := false
	for time.Now().Before(deadline) {
		var status string
		if err := db.QueryRowContext(ctx, "SELECT status FROM pve_runs WHERE id=?", runner.runID).Scan(&status); err != nil {
			return err
		}
		if status == "running" {
			started = true
			break
		}
		time.Sleep(200 * time.Millisecond)
	}
	if !started {
		return errors.New("approved UDP clients did not finish loading")
	}
	if *p3Stage && !(*human) {
		if *p4Stage {
			if err := runner.verifyP4(db, *scheme, *scenario, *fullFlow, *playerCount, *combatPressure); err != nil {
				return err
			}
			if *p5Stage && *fullFlow && *visual && *renderClients > 0 {
				if err := runner.waitP5ResultCaptures(*renderClients); err != nil {
					_ = os.Rename(filepath.Join(output, "summary.json"), filepath.Join(output, "gameplay-only-summary.json"))
					return err
				}
			}
			return nil
		}
		return runner.verifyP3(db, *scheme, *scenario, *fullFlow, *playerCount, *combatPressure)
	}
	if *fullFlow {
		return runner.verifyP2Full(db, *scheme, *scenario)
	}
	if *playerCount > 1 {
		return runner.verifyP2Multiplayer(db, *playerCount, *combatPressure)
	}
	if *p1V3 {
		if *human {
			if *p4Stage {
				fmt.Println("P4 ready. WASD, left mouse, Q/scroll, Space, E/F, R, J; Tab and 1/2/3 choose growth while the world continues; Esc solo pause.")
			} else if *p3Stage {
				fmt.Println("P3 ready. WASD, left mouse, Q/scroll switch, Space, E/F, R, J rescue/interact; Esc solo pause. Close to leave reliably.")
			} else {
				fmt.Println("P1 v3 ready. WASD, left/right mouse, Space, Q/F, R, E; close the player to end. No contracts, rewards or trial publication in this stage.")
			}
			playerProcess := runner.processes[len(runner.processes)-1]
			exited := make(chan error, 1)
			go func() { exited <- playerProcess.Wait() }()
			select {
			case <-ctx.Done():
				return errors.New("P1 session wall limit reached")
			case <-exited:
			}
			var status string
			if err := db.QueryRowContext(ctx, "SELECT status FROM pve_runs WHERE id=?", runner.runID).Scan(&status); err != nil {
				return err
			}
			if status == "running" {
				if *syncStage {
					runner.clients[0].socket.Close()
					header := http.Header{}
					header.Set("Authorization", "Bearer "+runner.clients[0].token)
					socket, _, err := websocket.DefaultDialer.DialContext(ctx, "ws://127.0.0.1:12880/ws", header)
					if err != nil {
						return err
					}
					runner.clients[0].socket = socket
				}

				if _, err := runner.command(runner.clients[0], "v2.run.leave", map[string]any{"run_id": runner.runID}); err != nil {
					return err
				}
			}
			for attempt := 0; attempt < 100; attempt++ {
				if err := db.QueryRowContext(ctx, "SELECT status FROM pve_runs WHERE id=?", runner.runID).Scan(&status); err != nil {
					return err
				}
				if status == "closed" {
					if *p3Stage {
						fmt.Println("P3 session closed; query own reliable result.")
					} else {
						fmt.Println("P1 session closed; no gameplay rewards.")
					}
					return nil
				}
				time.Sleep(100 * time.Millisecond)
			}
			return errors.New("P1 human exit settlement pending")
		}
		var proof struct {
			Run      string `json:"run_id"`
			Protocol int    `json:"protocol_version"`
			Ack      int64  `json:"input_ack"`
			Frame    int64  `json:"world_frame_id"`
			Damage   int64  `json:"damage"`
			Contexts int    `json:"contexts"`
			Real     bool   `json:"real_udp_world"`
		}
		verified := false
		for attempt := 0; attempt < 300; attempt++ {
			data, err := os.ReadFile(filepath.Join(output, "client-p1-v3.json"))
			if err == nil && json.Unmarshal(data, &proof) == nil && proof.Run == runner.runID && proof.Protocol == map[bool]int{true: map[bool]int{true: modernProtocol(), false: 5}[*v6Stage], false: map[bool]int{true: 4, false: 3}[*syncStage]}[*v5Stage] && proof.Ack > 0 && proof.Frame >= map[bool]int64{true: 30, false: 150}[*syncStage] && (*syncStage || proof.Damage > 0) && proof.Contexts == 6 && proof.Real {
				verified = true
				break
			}
			time.Sleep(100 * time.Millisecond)
		}
		if !verified {
			return errors.New("P1 real world/input/damage evidence missing")
		}
		if *syncStage {
			var probe map[string]bool
			probeFile := "client-s1-probe.json"
			if *weaponStage {
				probeFile = "client-s2-probe.json"
			}
			passed := false
			for attempt := 0; attempt < 200; attempt++ {
				data, err := os.ReadFile(filepath.Join(output, probeFile))
				if err == nil && json.Unmarshal(data, &probe) == nil && ((!*weaponStage && probe["paused"] && probe["world_frame_frozen"] && probe["player_state_frozen"] && probe["timescale_frozen"] && probe["resumed"] && probe["world_resumed"]) || (*weaponStage && probe["single_weapon_switch"] && probe["manual_charge_release"] && probe["primary_from_melee"] && probe["secondary_from_melee"] && probe["pause_new_skills"] && probe["resume_new_skills"] && probe["post_resume_action"])) {
					passed = true
					break
				}
				time.Sleep(100 * time.Millisecond)
			}
			if !passed {
				return errors.New("S1 real pause probe failed")
			}
		}
		if *syncStage {
			runner.clients[0].socket.Close()
			header := http.Header{}
			header.Set("Authorization", "Bearer "+runner.clients[0].token)
			socket, _, err := websocket.DefaultDialer.DialContext(ctx, "ws://127.0.0.1:12880/ws", header)
			if err != nil {
				return err
			}
			runner.clients[0].socket = socket
		}

		if _, err := runner.command(runner.clients[0], "v2.run.leave", map[string]any{"run_id": runner.runID}); err != nil {
			return err
		}
		for attempt := 0; attempt < 100; attempt++ {
			var status string
			if err := db.QueryRowContext(ctx, "SELECT status FROM pve_runs WHERE id=?", runner.runID).Scan(&status); err != nil {
				return err
			}
			if status == "closed" {
				var rewards int
				if err := db.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_reward_grants WHERE run_id=?", runner.runID).Scan(&rewards); err != nil || rewards != 0 {
					return errors.New("P1 stage granted gameplay rewards")
				}
				summary := map[string]any{"protocol_version": map[bool]int{true: map[bool]int{true: modernProtocol(), false: 5}[*v6Stage], false: map[bool]int{true: 4, false: 3}[*syncStage]}[*v5Stage], "run_id": runner.runID, "loadout": *scheme, "real_udp_world": true, "input_ack": proof.Ack, "world_frame_id": proof.Frame, "effective_damage": proof.Damage, "cleanup": "closed", "gameplay_rewards": false, "human_playtests": 0}
				if err := os.WriteFile(filepath.Join(output, "summary.json"), store.JSON(summary), 0600); err != nil {
					return err
				}
				fmt.Println(string(store.JSON(summary)))
				return nil
			}
			time.Sleep(100 * time.Millisecond)
		}
		return errors.New("P1 public leave did not close")
	}
	if *lobbyExpiry {
		var expired, confirmed, consumed int
		player := runner.clients[0].player
		if err := db.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_match_proposals p JOIN pve_match_proposal_members m ON m.proposal_id=p.id WHERE m.player_id=? AND p.status='rejected' AND m.response='pending'", player).Scan(&expired); err != nil {
			return err
		}
		if err := db.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_match_proposals WHERE run_id=? AND status='confirmed'", runner.runID).Scan(&confirmed); err != nil {
			return err
		}
		if err := db.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_dc_admissions WHERE run_id=? AND player_id=? AND status='consumed'", runner.runID, player).Scan(&consumed); err != nil {
			return err
		}
		if expired != 1 || confirmed != 1 || consumed != 1 {
			return errors.New("lobby facts do not match expiry, requeue and approved loading")
		}
		result := map[string]any{"scope": "P1_actual_Unity_lobby_expiry_recovery", "expired_proposals": expired, "confirmed_proposals": confirmed, "admissions_consumed": consumed, "run_id": runner.runID, "world_snapshot_received": true, "privileged_events_injected": false, "assets_mutated_for_verification": false}
		if err := os.WriteFile(filepath.Join(output, "summary.json"), store.JSON(result), 0600); err != nil {
			return err
		}
		fmt.Println(string(store.JSON(result)))
		return nil
	}
	var admissions int
	if err := db.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_dc_admissions WHERE run_id=? AND status='consumed'", runner.runID).Scan(&admissions); err != nil || admissions != count {
		return errors.New("four separate admissions were not consumed")
	}
	if *p1 {
		deadline = time.Now().Add(21 * time.Minute)
		for time.Now().Before(deadline) {
			var runStatus string
			if err := db.QueryRowContext(ctx, "SELECT status FROM pve_runs WHERE id=?", runner.runID).Scan(&runStatus); err != nil {
				return err
			}
			if runStatus == "closed" {
				break
			}
			time.Sleep(time.Second)
		}
		var stateJSON []byte
		if err := db.QueryRowContext(ctx, "SELECT state FROM pve_runs WHERE id=?", runner.runID).Scan(&stateJSON); err != nil {
			return err
		}
		var state struct {
			Status string `json:"status"`
			Reason string `json:"end_reason"`
		}
		if err := json.Unmarshal(stateJSON, &state); err != nil {
			return err
		}
		expectedReason := "success"
		if *scenario == "wipe" {
			expectedReason = "team_wipe"
		}
		if state.Status != "closed" || state.Reason != expectedReason {
			return fmt.Errorf("P1 run outcome: %s/%s", state.Status, state.Reason)
		}
		var rewards, ledger, damage, kills, deaths, interactions, frames, elapsedMS int64
		if err := db.QueryRowContext(ctx, "SELECT COALESCE(SUM(amount),0) FROM pve_reward_grants WHERE run_id=?", runner.runID).Scan(&rewards); err != nil {
			return err
		}
		if err := db.QueryRowContext(ctx, "SELECT COUNT(*) FROM asset_ledger WHERE pve_run_id=?", runner.runID).Scan(&ledger); err != nil {
			return err
		}
		if err := db.QueryRowContext(ctx, "SELECT damage,kills,deaths,interactions FROM pve_dc_participant_stats WHERE run_id=?", runner.runID).Scan(&damage, &kills, &deaths, &interactions); err != nil {
			return err
		}
		if err := db.QueryRowContext(ctx, "SELECT COUNT(*),COALESCE(MAX(JSON_EXTRACT(payload,'$.elapsed_ms')),0) FROM pve_dc_frame_receipts WHERE run_id=?", runner.runID).Scan(&frames, &elapsedMS); err != nil {
			return err
		}
		expectedReward := int64(100)
		if *scenario == "wipe" {
			expectedReward = 20
		}
		if rewards != expectedReward || ledger != 1 || damage <= 0 || *scenario == "success" && (kills <= 0 || interactions != 2) || *scenario == "wipe" && deaths != 4 {
			return errors.New("P1 stats or reliable reward mismatch")
		}
		result := map[string]any{"stage": "P1", "scenario": *scenario, "loadout": *scheme, "run_id": runner.runID, "outcome": state.Reason, "elapsed_ms": elapsedMS, "damage": damage, "kills": kills, "deaths": deaths, "interactions": interactions, "reward": rewards, "ledger_entries": ledger, "key_frames": frames, "real_authoritative_combat": true, "privileged_events_injected": false, "duration_target_met": *scenario == "success" && elapsedMS >= 600000 && elapsedMS <= 900000}
		if *lifecycle {
			var evidence struct {
				First       string `json:"first_run_id"`
				Second      string `json:"second_run_id"`
				Reconnected bool   `json:"reconnected"`
				SameSession bool   `json:"same_client_session"`
				Lobby       bool   `json:"returned_to_lobby"`
			}
			deadline = time.Now().Add(45 * time.Second)
			for time.Now().Before(deadline) {
				data, err := os.ReadFile(filepath.Join(output, "client-lifecycle.json"))
				if err == nil && json.Unmarshal(data, &evidence) == nil {
					break
				}
				time.Sleep(200 * time.Millisecond)
			}
			if evidence.First != runner.runID || evidence.Second == "" || evidence.Second == runner.runID || !evidence.Reconnected || !evidence.SameSession || !evidence.Lobby {
				return errors.New("same-client lifecycle incomplete")
			}
			deadline = time.Now().Add(15 * time.Second)
			var secondStatus, secondReason string
			for time.Now().Before(deadline) {
				if err := db.QueryRowContext(ctx, "SELECT status,COALESCE(end_reason,'') FROM pve_runs WHERE id=?", evidence.Second).Scan(&secondStatus, &secondReason); err != nil {
					return err
				}
				if secondStatus == "closed" {
					break
				}
				time.Sleep(200 * time.Millisecond)
			}
			if secondStatus != "closed" || secondReason != "team_abandoned" {
				return errors.New("second-run leave failed authority settlement")
			}
			var connected int64
			if err := db.QueryRowContext(ctx, "SELECT COALESCE(MAX(connection_generation),0) FROM pve_dc_admissions WHERE run_id=? AND status='consumed'", evidence.Second).Scan(&connected); err != nil || connected < 2 {
				return errors.New("second-run battle reconnect not consumed")
			}
			result["same_client_replay_reconnect_leave"] = true
			result["second_run_id"] = evidence.Second
			result["second_outcome"] = secondReason
		}
		if err := os.WriteFile(filepath.Join(output, "summary.json"), store.JSON(result), 0600); err != nil {
			return err
		}
		fmt.Println(string(store.JSON(result)))
		return nil
	}
	time.Sleep(1200 * time.Millisecond)
	var receipts int
	if err := db.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_dc_frame_receipts WHERE run_id=? AND status='applied'", runner.runID).Scan(&receipts); err != nil || receipts < 1 {
		return errors.New("service reliable phase frame not confirmed")
	}
	if err := runner.processes[2].Process.Kill(); err != nil {
		return err
	}
	time.Sleep(time.Second)
	var status string
	if err := db.QueryRowContext(ctx, "SELECT status FROM pve_runs WHERE id=?", runner.runID).Scan(&status); err != nil || status != "running" {
		return errors.New("client exit incorrectly terminated run")
	}
	operationHash := store.Hash([]byte(rand.Text()))
	admissionRequest := map[string]any{"operation_id": fmt.Sprintf("%s-%s-%s-%s-%s", operationHash[:8], operationHash[8:12], operationHash[12:16], operationHash[16:20], operationHash[20:32])}
	if *v3P0 {
		content, err := gameplay.LoadContent(os.Getenv("DC_CONFIG_PATH"))
		if err != nil {
			return err
		}
		admissionRequest["versions"] = content.V3
		admissionRequest["config_hash"] = content.Hash
	}
	response, err := runner.request("/api/v2/runs/"+runner.runID+"/admissions", runner.clients[0].token, admissionRequest)
	if err != nil {
		return err
	}
	var admission struct {
		Generation int64 `json:"connection_generation"`
	}
	json.Unmarshal(response, &admission)
	if admission.Generation < 2 {
		return errors.New("reconnect admission did not advance generation")
	}
	reconnectArguments := []string{"-dc-user", runner.clients[0].username, "-dc-password-file", "D:/dev-cache/dungeon-contract/private/p0-client-0.txt", "-dc-run", runner.runID}
	if *v3P0 {
		reconnectArguments = append(reconnectArguments, "-dc-runtime", "v3", "-dc-p0-evidence", filepath.Join(output, "client-reconnect-v3.json"))
	}
	if err := runner.launch(playerExe, "client-reconnect", reconnectArguments); err != nil {
		return err
	}
	deadline = time.Now().Add(22 * time.Second)
	reconnected := false
	var restoredGeneration int64
	for time.Now().Before(deadline) {
		err := db.QueryRowContext(ctx, "SELECT COALESCE(MAX(connection_generation),0) FROM pve_dc_admissions WHERE run_id=? AND player_id=? AND status='consumed'", runner.runID, runner.clients[0].player).Scan(&restoredGeneration)
		if err != nil {
			return err
		}
		if restoredGeneration >= admission.Generation {
			reconnected = true
			break
		}
		time.Sleep(200 * time.Millisecond)
	}
	if !reconnected {
		return errors.New("actual reconnect was not approved")
	}
	if *v3P0 {
		for index := 1; index < count; index++ {
			if err := runner.waitV3Evidence(filepath.Join(output, fmt.Sprintf("client-%d-v3.json", index))); err != nil {
				return err
			}
		}
		if err := runner.waitV3Evidence(filepath.Join(output, "client-reconnect-v3.json")); err != nil {
			return err
		}
		if _, err := runner.request("/api/v2/runs/"+runner.runID+"/admissions", runner.clients[0].token, map[string]any{"operation_id": uuidString()}); err == nil {
			return errors.New("legacy player admitted to v3")
		}
	}
	for _, account := range runner.clients {
		if _, err := runner.command(account, "v2.run.leave", map[string]any{"run_id": runner.runID}); err != nil {
			return fmt.Errorf("public cleanup leave failed: %w", err)
		}
	}
	settled := false
	for attempt := 0; attempt < 50; attempt++ {
		var status string
		if err := db.QueryRowContext(ctx, "SELECT status FROM pve_runs WHERE id=?", runner.runID).Scan(&status); err != nil {
			return err
		}
		if status == "closed" {
			settled = true
			break
		}
		time.Sleep(100 * time.Millisecond)
	}
	if !settled {
		return errors.New("reliable cleanup did not close run")
	}
	result := map[string]any{"run_id": runner.runID, "separate_game_service": true, "real_udp_clients": 4, "admissions_consumed": admissions, "configuration_hash_checked": true, "loaded_before_running": true, "phase_receipts": receipts, "client_exit_does_not_end_run": true, "reconnect_generation": restoredGeneration, "actual_udp_reconnect": true, "combat_content": "disabled_in_this_control_only_verification", "cleanup_run_status": "closed"}
	if *v3P0 {
		serverData, err := os.ReadFile(filepath.Join(output, "server-v3.json"))
		var serverEvidence struct {
			Role     string `json:"role"`
			NoUI     bool   `json:"no_ui"`
			Contexts int    `json:"contexts"`
		}
		if err != nil || json.Unmarshal(serverData, &serverEvidence) != nil || serverEvidence.Role != "server" || !serverEvidence.NoUI || serverEvidence.Contexts != 0 {
			return errors.New("standalone service created client UI or input maps")
		}
		result["protocol_version"] = 3
		result["service_without_ui_or_input_maps"] = true
		result["version_binding_checked"] = true
		result["old_client_rejected"] = true
		result["input_system_contexts"] = 6
		result["real_input_ack"] = true
		result["unimplemented_action_rejected"] = true
		var clocks int
		if err := db.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_dc_v3_run_clocks WHERE run_id=? AND protocol_version=3", runner.runID).Scan(&clocks); err != nil || clocks != 1 {
			return errors.New("v3 clock fact missing")
		}
		result["clock_fact_saved"] = true
	}
	if err := os.WriteFile(filepath.Join(output, "summary.json"), store.JSON(result), 0600); err != nil {
		return err
	}
	fmt.Println(string(store.JSON(result)))
	return nil
}

func (runner *harness) waitV3Evidence(path string) error {
	for attempt := 0; attempt < 50; attempt++ {
		data, err := os.ReadFile(path)
		var evidence struct {
			Protocol       int   `json:"protocol_version"`
			Loaded         bool  `json:"loaded"`
			Valid          bool  `json:"valid_binding"`
			InputAck       int64 `json:"input_ack"`
			Contexts       int   `json:"contexts"`
			ActionRejected bool  `json:"unimplemented_action_rejected"`
			NoSecrets      bool  `json:"no_service_secrets"`
		}
		if err == nil && json.Unmarshal(data, &evidence) == nil && evidence.Protocol == 3 && evidence.Loaded && evidence.Valid && evidence.InputAck > 0 && evidence.Contexts == 6 && evidence.ActionRejected && evidence.NoSecrets {
			return nil
		}
		time.Sleep(100 * time.Millisecond)
	}
	return errors.New("Unity v3 input/role/receipt evidence missing")
}
func (runner *harness) request(path, token string, value any) ([]byte, error) {
	request, err := http.NewRequestWithContext(runner.ctx, "POST", "http://127.0.0.1:12880"+path, bytes.NewReader(store.JSON(value)))
	if err != nil {
		return nil, err
	}
	request.Header.Set("Content-Type", "application/json")
	if token != "" {
		request.Header.Set("Authorization", "Bearer "+token)
	}
	response, err := runner.http.Do(request)
	if err != nil {
		return nil, errors.New("public HTTP unavailable")
	}
	defer response.Body.Close()
	data, err := io.ReadAll(io.LimitReader(response.Body, 65536))
	if response.StatusCode != 200 && response.StatusCode != 201 {
		return nil, fmt.Errorf("public HTTP %s rejected: %d", path, response.StatusCode)
	}
	return data, err
}
func (runner *harness) command(account *client, kind string, data any) (json.RawMessage, error) {
	id := store.ID("cmd")
	account.socket.SetReadDeadline(time.Now().Add(60 * time.Second))
	if err := account.socket.WriteJSON(map[string]any{"schema_version": 2, "type": kind, "request_id": id, "operation_id": id, "data": data}); err != nil {
		return nil, err
	}
	for {
		var message struct {
			Type    string          `json:"type"`
			Request string          `json:"request_id"`
			Code    int             `json:"code"`
			Data    json.RawMessage `json:"data"`
		}
		if err := account.socket.ReadJSON(&message); err != nil {
			return nil, errors.New("control command response unavailable")
		}
		if message.Type == "v2.match.proposal" {
			var proposal struct {
				ID string `json:"proposal_id"`
			}
			json.Unmarshal(message.Data, &proposal)
			account.proposal = proposal.ID
		}
		if message.Request == id {
			if message.Code != 0 {
				return nil, fmt.Errorf("control command %s rejected: %d", kind, message.Code)
			}
			return message.Data, nil
		}
	}
}
func (runner *harness) waitProposal(account *client) error {
	account.socket.SetReadDeadline(time.Now().Add(60 * time.Second))
	for {
		var message struct {
			Type string `json:"type"`
			Data struct {
				ID string `json:"proposal_id"`
			} `json:"data"`
		}
		if err := account.socket.ReadJSON(&message); err != nil {
			return errors.New("candidate notification unavailable")
		}
		if message.Type == "v2.match.proposal" {
			account.proposal = message.Data.ID
			return nil
		}
	}
}
func (runner *harness) launch(binary, role string, extra []string, keepCredentials ...bool) error {
	arguments := []string{"-dc-role", strings.Split(role, "-")[0], "-logFile", filepath.Join(runner.output, role+".log")}
	if modernProtocol() >= 7 {
		arguments = append([]string{"-job-worker-count", "2"}, arguments...)
	}
	if !containsArgument(extra, "-dc-visual") {
		arguments = append([]string{"-batchmode", "-nographics"}, arguments...)
	}
	if !containsArgument(extra, "-dc-battle") {
		arguments = append(arguments, "-dc-battle", "false")
	}
	arguments = append(arguments, extra...)
	command := exec.CommandContext(runner.ctx, binary, arguments...)
	environment := []string{}
	for _, value := range os.Environ() {
		name := strings.ToUpper(strings.SplitN(value, "=", 2)[0])
		if (len(keepCredentials) == 0 || !keepCredentials[0]) && (strings.Contains(name, "PASSWORD") || strings.Contains(name, "SECRET") || strings.Contains(name, "TOKEN") || strings.Contains(name, "API_KEY") || name == "DC_ADMISSION_KEY") {
			continue
		}
		environment = append(environment, value)
	}
	command.Env = environment
	log, err := os.OpenFile(filepath.Join(runner.output, role+"-process.log"), os.O_CREATE|os.O_WRONLY|os.O_EXCL, 0600)
	if err != nil {
		return err
	}
	defer log.Close()
	command.Stdout = log
	command.Stderr = log
	startedBefore := time.Now()
	if err := command.Start(); err != nil {
		return errors.New("role process failed to start")
	}
	runner.processes = append(runner.processes, command)
	if err := runner.writeProcessRoster(binary, role, command.Process.Pid, startedBefore, time.Now()); err != nil {
		return err
	}
	return nil
}
func containsArgument(values []string, value string) bool {
	for _, item := range values {
		if item == value {
			return true
		}
	}
	return false
}
func uuidString() string {
	data := make([]byte, 16)
	if _, err := rand.Read(data); err != nil {
		panic(err)
	}
	return fmt.Sprintf("%x-%x-%x-%x-%x", data[:4], data[4:6], data[6:8], data[8:10], data[10:])
}
func (runner *harness) cleanup() {
	_ = runner.finishMemoryObserver()
	for _, relay := range runner.udpRelays {
		_ = relay.Close()
	}
	for _, process := range runner.processes {
		if process.Process != nil {
			_ = process.Process.Kill()
			_ = process.Wait()
		}
	}
	for _, account := range runner.clients {
		if account.socket != nil {
			account.socket.Close()
		}
	}
	for index := range runner.clients {
		_ = os.Remove(filepath.Join("D:/dev-cache/dungeon-contract/private", fmt.Sprintf("p0-client-%d.txt", index)))
	}
	if runner.runID != "" {
		configuration := config.Load()
		db, err := database.NewMySQLDB(context.Background(), configuration.Database)
		if err == nil {
			defer db.Close()
			_, _ = db.Exec("UPDATE pve_dc_instances SET lease_until=UTC_TIMESTAMP(3)-INTERVAL 20 SECOND WHERE current_assignment_id IN (SELECT assignment_id FROM pve_dc_allocations WHERE run_id=?)", runner.runID)
		}
	}
}
