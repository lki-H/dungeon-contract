package main

import (
	"bytes"
	"context"
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"os/signal"
	"path/filepath"
	"strings"
	"sync/atomic"
	"syscall"
	"time"

	"game-realtime-gm/backend/internal/dclan"
	"github.com/gorilla/websocket"
)

type identity struct {
	Username string `json:"username"`
	Password string `json:"password"`
}
type message struct {
	Type    string          `json:"type"`
	Request string          `json:"request_id"`
	Code    int             `json:"code"`
	Data    json.RawMessage `json:"data"`
}
type player struct {
	ctx     context.Context
	http    *http.Client
	base    string
	socket  *websocket.Conn
	token   string
	run     string
	runtime string
}

func main() {
	host := flag.String("host", "", "host LAN or Radmin IPv4")
	loadout := flag.String("loadout", "ranger", "ranger or runesmith")
	contract := flag.String("contract", "", "optional contract key")
	root := flag.String("root", ".", "portable player folder")
	profile := flag.String("profile", "", "private per-player identity folder")
	autoplay := flag.Bool("autoplay", false, "engineering input-only test; never human acceptance")
	visual := flag.Bool("visual", true, "show Unity window")
	runtime := flag.String("runtime", "v4", "v4 or v5 matching the complete package")
	windowWidth := flag.Int("window-width", 0, "optional client window width")
	windowHeight := flag.Int("window-height", 0, "optional client window height")
	flag.Parse()
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	if err := execute(ctx, *host, *loadout, *contract, *root, *profile, *autoplay, *visual, *runtime, *windowWidth, *windowHeight); err != nil {
		fmt.Fprintln(os.Stderr, "Demo player:", err)
		os.Exit(1)
	}
}
func request(ctx context.Context, client *http.Client, base, path, token string, input any) ([]byte, error) {
	method := http.MethodGet
	var body io.Reader
	if input != nil {
		method = http.MethodPost
		data, err := json.Marshal(input)
		if err != nil {
			return nil, err
		}
		body = bytes.NewReader(data)
	}
	req, err := http.NewRequestWithContext(ctx, method, base+path, body)
	if err != nil {
		return nil, err
	}
	if input != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	if token != "" {
		req.Header.Set("Authorization", "Bearer "+token)
	}
	resp, err := client.Do(req)
	if err != nil {
		return nil, errors.New("cannot reach player TCP gateway; check host IPv4, VPN, host services and scoped firewall")
	}
	defer resp.Body.Close()
	data, err := io.ReadAll(io.LimitReader(resp.Body, 65537))
	if err != nil || len(data) > 65536 {
		return nil, errors.New("player response exceeds budget")
	}
	if resp.StatusCode != 200 && resp.StatusCode != 201 {
		return nil, fmt.Errorf("player request %s rejected HTTP %d", path, resp.StatusCode)
	}
	return data, nil
}
func loadIdentity(path string) (identity, bool, error) {
	var account identity
	data, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return identity{Username: "dclan_" + rand.Text(), Password: rand.Text() + rand.Text()}, true, nil
	}
	if err != nil || len(data) > 4096 {
		return account, false, errors.New("private player identity is unreadable; do not delete it to reset progress")
	}
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if decoder.Decode(&account) != nil || decoder.Decode(new(any)) != io.EOF || len(account.Username) < 8 || len(account.Username) > 64 || len(account.Password) < 16 || len(account.Password) > 128 {
		return account, false, errors.New("private player identity is invalid; original file preserved")
	}
	return account, false, nil
}
func exclusiveJSON(path string, value any) error {
	file, err := os.OpenFile(path, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
	if err != nil {
		return err
	}
	defer file.Close()
	if err = json.NewEncoder(file).Encode(value); err != nil {
		return err
	}
	return file.Sync()
}
func (client *player) command(kind string, input any) (string, error) {
	key := "lan_" + rand.Text()
	return key, client.socket.WriteJSON(map[string]any{"schema_version": 2, "type": kind, "request_id": key, "operation_id": key, "data": input})
}
func (client *player) connect() error {
	header := http.Header{}
	header.Set("Authorization", "Bearer "+client.token)
	socket, _, err := (&websocket.Dialer{HandshakeTimeout: 10 * time.Second}).DialContext(client.ctx, strings.Replace(client.base, "http://", "ws://", 1)+"/ws", header)
	if err != nil {
		return errors.New("player WebSocket connection failed")
	}
	socket.SetReadLimit(65536)
	client.socket = socket
	return nil
}
func (client *player) waitRun(loadout, contract string) error {
	ruleVersion, taskVersion := "dc_ruins.v4.p4.1", "dc_ruins.v4.p3.1"
	if client.runtime == "v5" {
		ruleVersion, taskVersion = "dc_ruins.v5.p4.1", "dc_tasks.v5.p4.1"
	}
	selection := map[string]any{"loadout_id": loadout}
	if contract != "" {
		selection["task_key"] = contract
		selection["task_version"] = taskVersion
	}
	queued, err := client.command("v2.match.enqueue", map[string]any{"plan": map[string]any{"operation": "dc_ruins", "difficulty": "standard", "rule_version": ruleVersion, "fill_policy": "public", "allow_partial": true}, "task": selection})
	if err != nil {
		return err
	}
	fmt.Println("Waiting for players. This is a private test server: join within 30 seconds of each other. Confirmation is automatic.")
	client.socket.SetReadDeadline(time.Now().Add(2 * time.Minute))
	activeSocket := client.socket
	go func() { <-client.ctx.Done(); _ = activeSocket.Close() }()
	for {
		var event message
		if err = client.socket.ReadJSON(&event); err != nil {
			return errors.New("matchmaking wait ended; restart all players together after the host confirms the service is idle")
		}
		if event.Type == "server.error" || event.Code != 0 {
			return fmt.Errorf("match command rejected code %d; contract may be locked or an earlier Run is still active", event.Code)
		}
		if event.Request == queued {
			fmt.Println("Authenticated queue accepted.")
		}
		if event.Type == "v2.match.proposal" {
			var proposal struct {
				ID       string `json:"proposal_id"`
				Revision int    `json:"revision"`
			}
			if json.Unmarshal(event.Data, &proposal) != nil || proposal.ID == "" {
				return errors.New("invalid proposal")
			}
			if _, err = client.command("v2.match.proposal_confirm", map[string]any{"proposal_id": proposal.ID, "revision": proposal.Revision}); err != nil {
				return err
			}
			fmt.Println("Proposal received and confirmed; waiting for teammates.")
		}
		if event.Type == "v2.match.assigned" || strings.HasSuffix(event.Type, "proposal_confirm.result") {
			var assignment struct {
				Run string `json:"run_id"`
			}
			if json.Unmarshal(event.Data, &assignment) == nil && assignment.Run != "" {
				client.run = assignment.Run
				return nil
			}
		}
		if event.Type == "v2.match.paused" {
			return errors.New("queue paused; restart all players together")
		}
	}
}
func (client *player) leave() error {
	if err := client.connect(); err != nil {
		return err
	}
	defer client.socket.Close()
	key, err := client.command("v2.run.leave", map[string]any{"run_id": client.run})
	if err != nil {
		return err
	}
	client.socket.SetReadDeadline(time.Now().Add(10 * time.Second))
	for {
		var event message
		if client.socket.ReadJSON(&event) != nil {
			return errors.New("leave confirmation unavailable; short disconnect recovery rules continue")
		}
		if event.Request == key {
			if event.Code != 0 {
				return fmt.Errorf("leave rejected %d", event.Code)
			}
			return nil
		}
	}
}
func execute(ctx context.Context, host, loadout, contract, root, profile string, autoplay, visual bool, runtime string, width, height int) error {
	if runtime != "v4" && runtime != "v5" {
		return errors.New("unknown battle runtime")
	}
	width, height, windowErr := windowDimensions(runtime, width, height)
	if windowErr != nil {
		return windowErr
	}
	if _, err := dclan.Host(host); err != nil {
		return err
	}
	if loadout != "ranger" && loadout != "runesmith" {
		return errors.New("unknown loadout")
	}
	switch contract {
	case "", "dc_hunter", "dc_precision", "dc_scout", "dc_technician", "dc_inspector", "dc_survivor":
	default:
		return errors.New("unknown contract")
	}
	root, err := filepath.Abs(root)
	if err != nil {
		return err
	}
	exe := filepath.Join(root, "DungeonContract.exe")
	if info, err := os.Stat(exe); err != nil || info.IsDir() {
		return errors.New("run inside the complete portable player folder, not a single copied EXE")
	}
	configData, err := os.ReadFile(filepath.Join(root, "Content", "demo-"+runtime+"-p4.json"))
	if err != nil {
		return err
	}
	var config struct {
		ID       string `json:"definition_id"`
		Protocol int    `json:"protocol_version"`
	}
	expectedProtocol := 4
	if runtime == "v5" {
		expectedProtocol = 5
	}
	if json.Unmarshal(configData, &config) != nil || config.ID != "dc_ruins."+runtime+".p4.1" || config.Protocol != expectedProtocol {
		return errors.New("P4 profile missing or wrong")
	}
	if profile == "" {
		return errors.New("private profile directory must be supplied by the player script")
	}
	if err = os.MkdirAll(profile, 0700); err != nil {
		return err
	}
	profile, err = filepath.Abs(profile)
	if err != nil {
		return err
	}
	identityPath := filepath.Join(profile, "account.json")
	account, fresh, err := loadIdentity(identityPath)
	if err != nil {
		return err
	}
	client := &player{ctx: ctx, http: &http.Client{Timeout: 10 * time.Second, Transport: &http.Transport{Proxy: nil}}, base: "http://" + host + ":12880", runtime: runtime}
	if _, err = request(ctx, client.http, client.base, "/ready", "", nil); err != nil {
		return err
	}
	if fresh {
		if _, err = request(ctx, client.http, client.base, "/api/register", "", map[string]any{"username": account.Username, "password": account.Password, "nickname": "LAN playtester"}); err != nil {
			return err
		}
		if err = exclusiveJSON(identityPath, account); err != nil {
			return err
		}
	}
	data, err := request(ctx, client.http, client.base, "/api/login", "", map[string]any{"username": account.Username, "password": account.Password})
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
	if json.Unmarshal(data, &login) != nil || login.Data.Token == "" || login.Data.Player.ID <= 0 {
		return errors.New("login response invalid")
	}
	client.token = login.Data.Token
	fmt.Println("Player ID:", login.Data.Player.ID, "Loadout:", loadout, "Server:", host)
	if err = client.connect(); err != nil {
		return err
	}
	if err = client.waitRun(loadout, contract); err != nil {
		client.socket.Close()
		cleanupCtx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		cleanup := &player{ctx: cleanupCtx, http: client.http, base: client.base, token: client.token}
		if cleanup.connect() == nil {
			key, sendErr := cleanup.command("v2.match.cancel", map[string]any{})
			if sendErr == nil {
				_ = cleanup.socket.SetReadDeadline(time.Now().Add(3 * time.Second))
				for {
					var reply message
					if cleanup.socket.ReadJSON(&reply) != nil || reply.Request == key {
						break
					}
				}
			}
			cleanup.socket.Close()
		}
		return err
	}
	client.socket.Close()
	fmt.Println("Run:", client.run, "Launching real Unity player. WASD, mouse, Q, Space, E/F, R, J, Tab and 1/2/3. Multiplayer Esc does not pause.")
	passwordFile, err := os.CreateTemp(profile, "battle-password-*.txt")
	if err != nil {
		return err
	}
	passwordPath := passwordFile.Name()
	defer os.Remove(passwordPath)
	if _, err = passwordFile.WriteString(account.Password); err != nil {
		passwordFile.Close()
		return err
	}
	passwordFile.Close()
	evidenceRoot := filepath.Join(filepath.Dir(profile), "sessions")
	if err = os.MkdirAll(evidenceRoot, 0700); err != nil {
		return err
	}
	session := filepath.Join(evidenceRoot, time.Now().Format("20060102-150405")+"-"+rand.Text()[:8])
	if err = os.Mkdir(session, 0700); err != nil {
		return err
	}
	args := []string{"-dc-runtime", runtime, "-dc-stage", "p4", "-dc-role", "client", "-dc-server-host", host, "-dc-run", client.run, "-dc-user", account.Username, "-dc-password-file", passwordPath, "-dc-battle", "true", "-logFile", filepath.Join(session, "player.log"), "-dc-p4-evidence", filepath.Join(session, "world.json")}
	if runtime == "v7" || runtime == "v8" {
		args = append([]string{"-job-worker-count", "2"}, args...)
	}
	if visual {
		args = append(args, "-dc-visual", "true", "-force-d3d11", "-screen-fullscreen", "0", "-screen-width", fmt.Sprint(width), "-screen-height", fmt.Sprint(height))
	} else {
		args = append(args, "-batchmode", "-nographics")
	}
	if autoplay {
		args = append(args, "-dc-p2-autoplay", "true")
	}
	command := exec.CommandContext(ctx, exe, args...)
	command.Dir = root
	for _, entry := range os.Environ() {
		name := strings.ToUpper(strings.SplitN(entry, "=", 2)[0])
		if strings.Contains(name, "PASSWORD") || strings.Contains(name, "SECRET") || strings.Contains(name, "TOKEN") || strings.Contains(name, "API_KEY") || strings.HasPrefix(name, "DB_") || strings.HasPrefix(name, "REDIS_") || strings.HasPrefix(name, "DC_") {
			continue
		}
		command.Env = append(command.Env, entry)
	}
	started := time.Now()
	if err = command.Start(); err != nil {
		return errors.New("Unity process failed to start")
	}
	var automaticClose atomic.Bool
	watching, stopWatching := context.WithCancel(ctx)
	defer stopWatching()
	if autoplay {
		go func() {
			ticker := time.NewTicker(time.Second)
			defer ticker.Stop()
			for {
				select {
				case <-watching.Done():
					return
				case <-ticker.C:
					data, err := request(watching, client.http, client.base, "/api/v2/runs/"+client.run+"/results", client.token, nil)
					var result struct {
						Data struct {
							Status string `json:"status"`
						} `json:"data"`
					}
					if err == nil && json.Unmarshal(data, &result) == nil && result.Data.Status == "closed" {
						automaticClose.Store(true)
						_ = command.Process.Kill()
						return
					}
				}
			}
		}()
	}
	exitErr := command.Wait()
	stopWatching()
	if automaticClose.Load() {
		exitErr = nil
	}
	hash := sha256.Sum256([]byte(strings.ReplaceAll(string(configData), "\r\n", "\n")))
	record := map[string]any{"profile": config.ID, "config_hash": hex.EncodeToString(hash[:]), "run_id": client.run, "player_id": login.Data.Player.ID, "server_ipv4": host, "loadout": loadout, "human": false, "manual_input_mode": !autoplay, "human_participation": "must be recorded by the tester; input mode alone is not evidence", "input_automation": autoplay, "started_at": started.UTC(), "wall_ms": time.Since(started).Milliseconds(), "cross_pc_verified": false, "network_scope": "must be recorded by human; address alone does not establish cross-PC evidence", "player_exit_ok": exitErr == nil}
	cleanupCtx, cancelCleanup := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancelCleanup()
	resultData, resultErr := request(cleanupCtx, client.http, client.base, "/api/v2/runs/"+client.run+"/results", client.token, nil)
	var state struct {
		Data struct {
			Status string `json:"status"`
		} `json:"data"`
	}
	_ = json.Unmarshal(resultData, &state)
	if resultErr == nil && state.Data.Status == "closed" {
		_ = os.WriteFile(filepath.Join(session, "own-result.json"), resultData, 0600)
	} else {
		cleanup := &player{ctx: cleanupCtx, http: client.http, base: client.base, token: client.token, run: client.run}
		record["leave_confirmed"] = cleanup.leave() == nil
	}
	if err = exclusiveJSON(filepath.Join(session, "session.json"), record); err != nil {
		return err
	}
	fmt.Println("Session evidence:", session, "Keep the private account folder on this computer; do not share it.")
	if exitErr != nil {
		return errors.New("Unity exited abnormally; inspect this session player.log")
	}
	return nil
}
