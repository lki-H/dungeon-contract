package gameplay_test

import (
	"context"
	"crypto/rand"
	"database/sql"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"

	"game-realtime-gm/backend/internal/cache"
	"game-realtime-gm/backend/internal/config"
	"game-realtime-gm/backend/internal/database"
	"game-realtime-gm/backend/internal/pve"
	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/run"
	"game-realtime-gm/backend/internal/pve/store"
)

type fixture struct {
	ctx      context.Context
	app      *pve.App
	game     *gameplay.Service
	db       *sql.DB
	clock    time.Time
	state    *run.State
	manifest *gameplay.Manifest
	players  []int64
}

func setup(test *testing.T, count int) *fixture {
	test.Helper()
	if os.Getenv("DC_INTEGRATION") != "1" {
		test.Skip("DC_INTEGRATION=1 and dedicated Demo storage required")
	}
	configuration := config.Load()
	if err := config.ValidateDemo(configuration); err != nil {
		test.Fatal(err)
	}
	ctx := context.Background()
	db, err := database.NewMySQLDB(ctx, configuration.Database)
	if err != nil {
		test.Fatal(err)
	}
	redisClient, err := cache.NewRedisClient(ctx, configuration.Redis)
	if err != nil {
		db.Close()
		test.Fatal(err)
	}
	contentPath := filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v1.json")
	if os.Getenv("DC_QUALITY_TEST") == "1" {
		contentPath = filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v2.json")
	}
	if os.Getenv("DC_V3_P0_TEST") == "1" {
		contentPath = filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v3-p0.json")
	}
	if os.Getenv("DC_V3_P1_TEST") == "1" {
		contentPath = filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v3-p1.json")
	}
	if os.Getenv("DC_S1_TEST") == "1" {
		contentPath = filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v4-s1.json")
	}
	if os.Getenv("DC_S2_TEST") == "1" {
		contentPath = filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v4-s2.json")
	}
	if os.Getenv("DC_P2_MP_TEST") == "1" {
		contentPath = filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v4-p2-mp.json")
	}
	if os.Getenv("DC_P3_TEST") == "1" {
		contentPath = filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v4-p3.json")
	}
	if os.Getenv("DC_P4_TEST") == "1" {
		contentPath = filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v4-p4.json")
	}
	if os.Getenv("DC_V5_TEST") == "1" {
		contentPath = filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v5-p4.json")
	}
	if os.Getenv("DC_P5_TEST") == "1" {
		contentPath = filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v5-p5.json")
	}
	if os.Getenv("DC_V6_TEST") == "1" {
		contentPath = filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v6-p5.json")
	}
	if os.Getenv("DC_V7_TEST") == "1" {
		contentPath = filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v7-p5.json")
	}
	if os.Getenv("DC_V8_TEST") == "1" {
		contentPath = filepath.Join("..", "..", "..", "..", "docs", "game-demo", "config", "demo-v8-p5.json")
	}
	content, err := gameplay.LoadContent(contentPath)
	if err != nil {
		test.Fatal(err)
	}
	app := pve.New(db, redisClient, content.Rules)
	key, err := hex.DecodeString(os.Getenv("DC_ADMISSION_KEY"))
	if err != nil {
		test.Fatal(err)
	}
	if err := app.EnableGame(content, key); err != nil {
		test.Fatal(err)
	}
	if err := app.Publish(ctx); err != nil {
		test.Fatal(err)
	}
	fixture := &fixture{ctx: ctx, app: app, game: app.Game, db: db, clock: time.Now().UTC()}
	app.Game.Now = func() time.Time { return fixture.clock }
	app.Runs.Now = app.Game.Now
	app.Match.Now = app.Game.Now
	if err := app.Game.Register(ctx, gameplay.InstanceRequest{Versions: content.V3, InstanceID: "dc_local_01", BootID: uuid(), ProtocolVersion: content.ProtocolVersion(), ConfigVersions: []gameplay.ConfigVersion{{ID: content.Rules.Version, Hash: content.Hash}}}); err != nil {
		test.Fatal(err)
	}
	rows, err := db.QueryContext(ctx, "SELECT DISTINCT m.player_id FROM pve_match_ticket_members m JOIN pve_match_tickets t ON t.id=m.ticket_id JOIN players p ON p.id=m.player_id WHERE t.status='queued' AND p.password_hash='not_a_login_hash' AND p.nickname='P0 integration'")
	if err != nil {
		test.Fatal(err)
	}
	stale := []int64{}
	for rows.Next() {
		var player int64
		if err := rows.Scan(&player); err != nil {
			rows.Close()
			test.Fatal(err)
		}
		stale = append(stale, player)
	}
	err = rows.Err()
	rows.Close()
	if err != nil {
		test.Fatal(err)
	}
	for _, player := range stale {
		if _, err := app.Command(ctx, player, store.ID("cleanup"), "v2.match.cancel", store.JSON(map[string]any{})); err != nil {
			test.Fatal(err)
		}
	}
	for index := 0; index < count; index++ {
		result, err := db.ExecContext(ctx, "INSERT INTO players(username,password_hash,nickname,banned_reason) VALUES(?,'not_a_login_hash','P0 integration','')", store.ID("dc_test"))
		if err != nil {
			test.Fatal(err)
		}
		id, _ := result.LastInsertId()
		if _, err := db.ExecContext(ctx, "INSERT INTO player_assets(player_id) VALUES(?)", id); err != nil {
			test.Fatal(err)
		}
		fixture.players = append(fixture.players, id)
	}
	test.Cleanup(func() {
		if fixture.state != nil {
			generation := fixture.state.SourceGeneration
			if fixture.manifest != nil {
				generation = fixture.manifest.Generation
			}
			_ = app.Game.Abort(ctx, fixture.state.ID, generation)
			_ = app.Settlement.Settle(ctx, fixture.state.ID)
			_ = app.Game.Tick(ctx)
			_ = app.Worker.Once(ctx)
		}
		for _, player := range fixture.players {
			_, _ = app.Command(ctx, player, store.ID("cleanup"), "v2.match.cancel", store.JSON(map[string]any{}))
		}
		redisClient.Close()
		db.Close()
	})
	return fixture
}
func uuid() string {
	data := make([]byte, 16)
	if _, err := rand.Read(data); err != nil {
		panic(err)
	}
	return fmt.Sprintf("%x-%x-%x-%x-%x", data[:4], data[4:6], data[6:8], data[8:10], data[10:])
}
func (fixture *fixture) create(test *testing.T, taskKey string) {
	test.Helper()
	for _, player := range fixture.players {
		selection := map[string]any{"loadout_id": "ranger"}
		if taskKey != "" {
			selection["task_key"] = taskKey
			selection["task_version"] = fixture.app.Runs.Rules.Version
			if definition, exists := fixture.app.Runs.Rules.Task(taskKey); exists {
				selection["task_version"] = definition.Version
			}
		}
		plan := map[string]any{"operation": "dc_ruins", "difficulty": "standard", "rule_version": fixture.app.Runs.Rules.Version, "fill_policy": map[bool]string{true: "no_fill", false: "public"}[len(fixture.players) == 1], "allow_partial": true}
		if os.Getenv("DC_P5_TRIAL_TEST") == "1" {
			plan["mode"] = "trial"
			plan["trial_id"] = "dc_ruins_trial"
			plan["trial_version"] = "trial.v5.1"
		}
		_, err := fixture.app.Command(fixture.ctx, player, store.ID("enqueue"), "v2.match.enqueue", store.JSON(map[string]any{"plan": plan, "task": selection}))
		if err != nil {
			test.Fatal(err)
		}
	}
	if len(fixture.players) < 4 && len(fixture.players) > 1 {
		fixture.clock = fixture.clock.Add(31 * time.Second)
		if err := fixture.game.Register(fixture.ctx, gameplay.InstanceRequest{Versions: fixture.game.Content.V3, InstanceID: "dc_local_01", BootID: fixture.boot(test), ProtocolVersion: fixture.game.Content.ProtocolVersion(), ConfigVersions: []gameplay.ConfigVersion{{ID: fixture.app.Runs.Rules.Version, Hash: fixture.game.Content.Hash}}}); err != nil {
			test.Fatal(err)
		}
	}
	if err := fixture.app.Match.Match(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	var proposal string
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT proposal_id FROM pve_match_proposal_members WHERE player_id=? ORDER BY proposal_id DESC LIMIT 1", fixture.players[0]).Scan(&proposal); err != nil {
		test.Fatal(err)
	}
	for _, player := range fixture.players {
		result, err := fixture.app.Command(fixture.ctx, player, store.ID("confirm"), "v2.match.proposal_confirm", store.JSON(map[string]any{"proposal_id": proposal, "revision": 1}))
		if err != nil {
			test.Fatal(err)
		}
		var response struct {
			RunID string `json:"run_id"`
		}
		json.Unmarshal(result, &response)
		if response.RunID != "" {
			fixture.state, err = fixture.app.Runs.Snapshot(fixture.ctx, response.RunID)
			if err != nil {
				test.Fatal(err)
			}
		}
	}
	if fixture.state == nil {
		test.Fatal("no assigned run")
	}
	manifest, err := fixture.game.Claim(fixture.ctx, gameplay.InstanceRequest{Versions: fixture.game.Content.V3, BootID: fixture.boot(test)})
	if err != nil || manifest == nil {
		test.Fatal("manifest missing", err)
	}
	fixture.manifest = manifest
}
func (fixture *fixture) boot(test *testing.T) string {
	test.Helper()
	var boot string
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT boot_id FROM pve_dc_instances WHERE instance_id='dc_local_01'").Scan(&boot); err != nil {
		test.Fatal(err)
	}
	return boot
}
func (fixture *fixture) start(test *testing.T) {
	test.Helper()
	players := []gameplay.LoadedMember{}
	for slot, player := range fixture.players {
		admission, err := fixture.game.Issue(fixture.ctx, gameplay.Admission{Versions: fixture.game.Content.V3, Hash: fixture.game.Content.Hash, Player: player, RunID: fixture.state.ID, Operation: uuid()})
		if err != nil {
			test.Fatal(err)
		}
		_, err = fixture.game.Consume(fixture.ctx, gameplay.ConsumeRequest{Versions: fixture.game.Content.V3, Hash: fixture.game.Content.Hash, Ticket: admission.Ticket, Nonce: uuid(), Instance: "dc_local_01", Boot: fixture.manifest.BootID})
		if err != nil {
			test.Fatal(err)
		}
		players = append(players, gameplay.LoadedMember{Player: strconv.FormatInt(player, 10), Slot: slot, Connection: admission.Connection})
	}
	_, err := fixture.game.Start(fixture.ctx, fixture.state.ID, gameplay.StartRequest{Versions: fixture.game.Content.V3, Assignment: fixture.manifest.AssignmentID, Generation: fixture.manifest.Generation, Players: players, Hash: fixture.manifest.ConfigHash})
	if err != nil {
		test.Fatal(err)
	}
}
func (fixture *fixture) frame(sequence int64, facts []gameplay.Fact) *gameplay.Frame {
	return &gameplay.Frame{Schema: fixture.game.Content.ProtocolVersion(), Source: "unity_game_server", RunID: fixture.state.ID, Instance: "dc_local_01", Boot: fixture.manifest.BootID, Assignment: fixture.manifest.AssignmentID, Generation: fixture.manifest.Generation, Sequence: sequence, ID: uuid(), WorldFrame: sequence, Elapsed: 0, Facts: facts}
}
func encode(frame *gameplay.Frame) []byte {
	frame.Fingerprint = ""
	value, _ := gameplay.DecodeJSON(store.JSON(frame))
	object := value.(map[string]any)
	delete(object, "fingerprint")
	frame.Fingerprint = store.Hash(gameplay.Canonical(object))
	return store.JSON(frame)
}
func fact(player int64, kind, target string) gameplay.Fact {
	actor := strconv.FormatInt(player, 10)
	action := store.ID("action")
	eligible := true
	return gameplay.Fact{EventID: uuid(), Kind: kind, Actor: &actor, Target: target, Contributors: []string{}, Action: &action, Eligible: &eligible}
}
func phase() gameplay.Fact {
	return gameplay.Fact{EventID: uuid(), Kind: "phase", Target: "world_phase", Contributors: []string{}, Phase: "active"}
}
func TestPersistentAtomicFramesAndRewardRecovery(test *testing.T) {
	fixture := setup(test, 1)
	fixture.create(test, "dc_survivor")
	fixture.start(test)
	player := fixture.players[0]
	for index, target := range []string{"hub_a", "hub_b"} {
		frame := fixture.frame(int64(index+1), []gameplay.Fact{fact(player, "interact", target)})
		if _, err := fixture.game.Frame(fixture.ctx, encode(frame)); err != nil {
			test.Fatal(err)
		}
	}
	kill, death := fact(player, "kill", "boss"), fact(player, "death", "player_life_1")
	frame := fixture.frame(3, []gameplay.Fact{death, kill})
	frame.Terminal = &gameplay.Terminal{Outcome: "success", Reason: "objectives_complete", Frame: 3, Sequence: 3}
	frame.Summary = &gameplay.Summary{Participants: []gameplay.Stats{{PlayerID: strconv.FormatInt(player, 10), Slot: 0, LoadoutID: "ranger", Interactions: 2, Kills: 1, Deaths: 1, Choices: []string{}, Effective: true}}, Eligible: true, Reason: "none"}
	data := encode(frame)
	receipt, err := fixture.game.Frame(fixture.ctx, data)
	if err != nil || receipt.RunStatus != "ending" {
		test.Fatal(receipt, err)
	}
	duplicate, err := fixture.game.Frame(fixture.ctx, data)
	if err != nil || duplicate.Applied != 3 {
		test.Fatal("terminal replay", err)
	}
	state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.EndReason != "success" || state.Participants[0].Deaths != 1 || len(state.Spawns) != 0 {
		test.Fatal("atomic outcome", err)
	}
	if err := fixture.app.Settlement.Settle(fixture.ctx, state.ID); err != nil {
		test.Fatal(err)
	}
	if err := fixture.app.Settlement.Settle(fixture.ctx, state.ID); err != nil {
		test.Fatal(err)
	}
	var total, grants int64
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COALESCE(SUM(amount),0),COUNT(*) FROM pve_reward_grants WHERE run_id=?", state.ID).Scan(&total, &grants); err != nil {
		test.Fatal(err)
	}
	if total != 100 || grants != 1 {
		test.Fatal("reward duplication or no-death reward", total, grants)
	}
	frame.Facts[1].Target = "other_boss"
	if _, err := fixture.game.Frame(fixture.ctx, encode(frame)); !errors.Is(err, store.Conflict) {
		test.Fatal("changed receipt accepted", err)
	}
}
func TestAdmissionsControlsAndOutOfOrderFrames(test *testing.T) {
	fixture := setup(test, 1)
	fixture.create(test, "")
	player := fixture.players[0]
	input := gameplay.Admission{Player: player, RunID: fixture.state.ID, Operation: uuid()}
	first, err := fixture.game.Issue(fixture.ctx, input)
	if err != nil {
		test.Fatal(err)
	}
	repeat, err := fixture.game.Issue(fixture.ctx, input)
	if err != nil || first.Ticket != repeat.Ticket {
		test.Fatal("admission idempotency")
	}
	nonce := uuid()
	consume := gameplay.ConsumeRequest{Ticket: first.Ticket, Nonce: nonce, Instance: "dc_local_01", Boot: fixture.manifest.BootID}
	if _, err := fixture.game.Consume(fixture.ctx, consume); err != nil {
		test.Fatal(err)
	}
	if _, err := fixture.game.Consume(fixture.ctx, consume); err != nil {
		test.Fatal(err)
	}
	consume.Nonce = uuid()
	if _, err := fixture.game.Consume(fixture.ctx, consume); !errors.Is(err, store.Conflict) {
		test.Fatal("ticket reuse", err)
	}
	fixture.start(test)
	future := fixture.frame(2, []gameplay.Fact{phase()})
	receipt, err := fixture.game.Frame(fixture.ctx, encode(future))
	if err != nil || receipt.Status != "received" {
		test.Fatal("future not buffered", err)
	}
	firstFrame := fixture.frame(1, []gameplay.Fact{phase()})
	if _, err := fixture.game.Frame(fixture.ctx, encode(firstFrame)); err != nil {
		test.Fatal(err)
	}
	state, _ := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if state.AppliedSequence != 2 {
		test.Fatal("gap drain")
	}
	if _, err := fixture.app.Command(fixture.ctx, player, store.ID("leave"), "v2.run.leave", store.JSON(map[string]any{"run_id": state.ID})); err != nil {
		test.Fatal(err)
	}
	stale := fixture.frame(3, []gameplay.Fact{phase()})
	if _, err := fixture.game.Frame(fixture.ctx, encode(stale)); !errors.Is(err, gameplay.Fence) {
		test.Fatal("control fence bypass", err)
	}
}
func TestGapTimeoutSourceReplacementAndLastReinforcement(test *testing.T) {
	fixture := setup(test, 1)
	fixture.create(test, "")
	fixture.start(test)
	future := fixture.frame(2, []gameplay.Fact{phase()})
	if _, err := fixture.game.Frame(fixture.ctx, encode(future)); err != nil {
		test.Fatal(err)
	}
	fixture.clock = fixture.clock.Add(6 * time.Second)
	if err := fixture.game.Tick(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	state, _ := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if state.EndReason != "event_gap" {
		test.Fatal("gap did not end run")
	}
}

func TestMigrationChecksumsAndPartialDDLRejection(test *testing.T) {
	fixture := setup(test, 1)
	root := filepath.Join("..", "..", "database", "migrations")
	path := filepath.Join(root, "day41_dc_gameplay_control.sql")
	if _, err := database.ApplyDemoControl(fixture.ctx, fixture.db, path); err != nil {
		test.Fatal(err)
	}
	data, err := os.ReadFile(path)
	if err != nil {
		test.Fatal(err)
	}
	changed := filepath.Join(test.TempDir(), "changed.sql")
	os.WriteFile(changed, append(data, []byte("\n ")...), 0600)
	if _, err := database.ApplyDemoControl(fixture.ctx, fixture.db, changed); err == nil || !strings.Contains(err.Error(), "checksum") {
		test.Fatal("changed migration accepted", err)
	}
}

func TestLastReinforcementAndSupersededOrBannedAdmission(test *testing.T) {
	fixture := setup(test, 4)
	fixture.create(test, "")
	fixture.start(test)
	first := fixture.frame(1, []gameplay.Fact{fact(fixture.players[0], "death", "life_a"), fact(fixture.players[1], "death", "life_b")})
	receipt, err := fixture.game.Frame(fixture.ctx, encode(first))
	if err != nil || len(receipt.Reinforcements) != 2 {
		test.Fatal("initial reinforcement reservation", err)
	}
	facts := []gameplay.Fact{}
	for action, spawn := range receipt.Reinforcements {
		value := fact(spawn.PlayerID, "reinforcement_spawned", "safe_point")
		value.Reinforcement = action
		facts = append(facts, value)
	}
	if _, err := fixture.game.Frame(fixture.ctx, encode(fixture.frame(2, facts))); err != nil {
		test.Fatal(err)
	}
	last := fixture.frame(3, []gameplay.Fact{fact(fixture.players[1], "death", "life_c"), fact(fixture.players[0], "death", "life_d")})
	if _, err := fixture.game.Frame(fixture.ctx, encode(last)); err != nil {
		test.Fatal(err)
	}
	state, _ := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	pending := 0
	var reservedPlayer int64
	for _, spawn := range state.Spawns {
		if spawn.Status == "pending_spawn" {
			pending++
			reservedPlayer = spawn.PlayerID
		}
	}
	if state.ReinforcementUsed != 2 || pending != 1 || reservedPlayer != fixture.players[0] {
		test.Fatal("last budget or slot order incorrect")
	}
	player := fixture.players[2]
	old, err := fixture.game.Issue(fixture.ctx, gameplay.Admission{Player: player, RunID: state.ID, Operation: uuid()})
	if err != nil {
		test.Fatal(err)
	}
	_, err = fixture.game.Issue(fixture.ctx, gameplay.Admission{Player: player, RunID: state.ID, Operation: uuid()})
	if err != nil {
		test.Fatal(err)
	}
	if _, err := fixture.game.Consume(fixture.ctx, gameplay.ConsumeRequest{Ticket: old.Ticket, Nonce: uuid(), Instance: "dc_local_01", Boot: fixture.manifest.BootID}); !errors.Is(err, gameplay.Expired) {
		test.Fatal("superseded ticket accepted", err)
	}
	if _, err := fixture.db.ExecContext(fixture.ctx, "UPDATE players SET status='banned' WHERE id=?", player); err != nil {
		test.Fatal(err)
	}
	if _, err := fixture.game.Issue(fixture.ctx, gameplay.Admission{Player: player, RunID: state.ID, Operation: uuid()}); !errors.Is(err, store.Forbidden) {
		test.Fatal("banned player got admission", err)
	}
}

func TestOldSourceLeaseAndCapacityReservation(test *testing.T) {
	fixture := setup(test, 1)
	fixture.create(test, "")
	fixture.start(test)
	frame := fixture.frame(1, []gameplay.Fact{phase()})
	frame.Generation++
	if _, err := fixture.game.Frame(fixture.ctx, encode(frame)); !errors.Is(err, store.Forbidden) {
		test.Fatal("invalid source generation", err)
	}
	fixture.clock = fixture.clock.Add(17 * time.Second)
	if err := fixture.game.Tick(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	state, _ := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if state.EndReason != "system_abort" {
		test.Fatal("expired lease not aborted")
	}
}

func TestCapacityFailureReturnsSourcesWithoutBlamingPlayer(test *testing.T) {
	fixture := setup(test, 1)
	player := fixture.players[0]
	if _, err := fixture.app.Command(fixture.ctx, player, store.ID("enqueue"), "v2.match.enqueue", store.JSON(map[string]any{"plan": map[string]any{"operation": "dc_ruins", "difficulty": "standard", "rule_version": fixture.app.Runs.Rules.Version, "fill_policy": "no_fill"}, "task": map[string]any{"loadout_id": "ranger"}})); err != nil {
		test.Fatal(err)
	}
	if err := fixture.app.Match.Match(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	fixture.clock = fixture.clock.Add(7 * time.Second)
	if err := fixture.game.Tick(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	var status string
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT status FROM pve_match_tickets WHERE id=(SELECT activity_id FROM pve_player_activity_locks WHERE player_id=?)", player).Scan(&status); err != nil || status != "queued" {
		test.Fatal("system failure blamed player", err)
	}
	if _, err := fixture.app.Command(fixture.ctx, player, store.ID("cancel"), "v2.match.cancel", store.JSON(map[string]any{})); err != nil {
		test.Fatal(err)
	}
}

func TestAbandonedTerminalAndBufferedControlFence(test *testing.T) {
	fixture := setup(test, 1)
	fixture.create(test, "")
	fixture.start(test)
	buffered := fixture.frame(2, []gameplay.Fact{phase()})
	if _, err := fixture.game.Frame(fixture.ctx, encode(buffered)); err != nil {
		test.Fatal(err)
	}
	if _, err := fixture.app.Command(fixture.ctx, fixture.players[0], store.ID("leave"), "v2.run.leave", store.JSON(map[string]any{"run_id": fixture.state.ID})); err != nil {
		test.Fatal(err)
	}
	if _, err := fixture.game.Frame(fixture.ctx, encode(buffered)); !errors.Is(err, gameplay.Fence) {
		test.Fatal("old control frame accepted", err)
	}
	frame := fixture.frame(1, []gameplay.Fact{phase()})
	frame.Control = 1
	frame.Terminal = &gameplay.Terminal{Outcome: "failed", Reason: "team_abandoned", Frame: 1, Sequence: 1}
	frame.Summary = &gameplay.Summary{Participants: []gameplay.Stats{{PlayerID: strconv.FormatInt(fixture.players[0], 10), Slot: 0, LoadoutID: "ranger", Choices: []string{}}}, Reason: "debug"}
	if _, err := fixture.game.Frame(fixture.ctx, encode(frame)); err != nil {
		test.Fatal("last-member leave became technical abort", err)
	}
	state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.EndReason != "team_abandoned" {
		test.Fatal("incorrect abandon reason", err)
	}
	if err := fixture.app.Settlement.Settle(fixture.ctx, state.ID); err != nil {
		test.Fatal(err)
	}
	if err := fixture.game.Tick(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	manifest, err := fixture.game.Claim(fixture.ctx, gameplay.InstanceRequest{BootID: fixture.boot(test)})
	if err != nil || manifest != nil {
		test.Fatal("instance not reusable after settlement", err)
	}
}

func TestPartialLoadingReturnsHealthySourceToQueue(test *testing.T) {
	fixture := setup(test, 2)
	fixture.create(test, "")
	player := fixture.players[0]
	admission, err := fixture.game.Issue(fixture.ctx, gameplay.Admission{Player: player, RunID: fixture.state.ID, Operation: uuid()})
	if err != nil {
		test.Fatal(err)
	}
	if _, err := fixture.game.Consume(fixture.ctx, gameplay.ConsumeRequest{Ticket: admission.Ticket, Nonce: uuid(), Instance: "dc_local_01", Boot: fixture.manifest.BootID}); err != nil {
		test.Fatal(err)
	}
	input := gameplay.StartRequest{ReadyOnly: true, Assignment: fixture.manifest.AssignmentID, Generation: fixture.manifest.Generation, Hash: fixture.manifest.ConfigHash, Players: []gameplay.LoadedMember{{Player: strconv.FormatInt(player, 10), Slot: 0, Connection: admission.Connection}}}
	if _, err := fixture.game.Start(fixture.ctx, fixture.state.ID, input); err != nil {
		test.Fatal(err)
	}
	state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
	if err != nil || state.Status != "loading" {
		test.Fatal("partial readiness started battle", err)
	}
	fixture.clock = state.DeadlineAt.Add(time.Millisecond)
	if _, err := fixture.db.ExecContext(fixture.ctx, "UPDATE pve_dc_instances SET lease_until=? WHERE instance_id='dc_local_01'", fixture.clock.Add(time.Minute)); err != nil {
		test.Fatal(err)
	}
	if err := fixture.app.Tick(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	state, err = fixture.app.Runs.Snapshot(fixture.ctx, state.ID)
	if err != nil || state.Status != "aborted" || state.EndReason != "loading_failed" {
		test.Fatal("loading failure incorrectly settled", err)
	}
	for slot, member := range state.Participants {
		var status string
		if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT status FROM pve_match_tickets WHERE id=?", member.TicketID).Scan(&status); err != nil {
			test.Fatal(err)
		}
		want := "canceled"
		if slot == 0 {
			want = "queued"
		}
		if status != want {
			test.Fatalf("source %d status %q; want %q", slot, status, want)
		}
	}
}

func TestAuthorityTimeoutAndTechnicalAbortKeepRewardRules(test *testing.T) {
	for _, reason := range []string{"timeout", "system_abort"} {
		test.Run(reason, func(subtest *testing.T) {
			fixture := setup(subtest, 1)
			fixture.create(subtest, "")
			fixture.start(subtest)
			player := fixture.players[0]
			if _, err := fixture.game.Frame(fixture.ctx, encode(fixture.frame(1, []gameplay.Fact{fact(player, "interact", "hub_a")}))); err != nil {
				subtest.Fatal(err)
			}
			expected := int64(0)
			if reason == "timeout" {
				fixture.clock = fixture.clock.Add(time.Duration(fixture.game.Content.Rules.Duration) * time.Second)
				if _, err := fixture.db.ExecContext(fixture.ctx, "UPDATE pve_dc_instances SET lease_until=? WHERE instance_id='dc_local_01'", fixture.clock.Add(time.Minute)); err != nil {
					subtest.Fatal(err)
				}
				frame := fixture.frame(2, []gameplay.Fact{phase()})
				frame.Elapsed = int64(fixture.game.Content.Rules.Duration) * 1000
				frame.Terminal = &gameplay.Terminal{Outcome: "failed", Reason: "timeout", Frame: 2, Sequence: 2}
				frame.Summary = &gameplay.Summary{Participants: []gameplay.Stats{{PlayerID: strconv.FormatInt(player, 10), LoadoutID: "ranger", Interactions: 1, Effective: true, Choices: []string{}}}, Reason: "debug"}
				if _, err := fixture.game.Frame(fixture.ctx, encode(frame)); err != nil {
					subtest.Fatal(err)
				}
				expected = fixture.game.Content.Rules.FailureReward
			} else if err := fixture.game.Abort(fixture.ctx, fixture.state.ID, fixture.manifest.Generation); err != nil {
				subtest.Fatal(err)
			}
			if err := fixture.app.Settlement.Settle(fixture.ctx, fixture.state.ID); err != nil {
				subtest.Fatal(err)
			}
			state, err := fixture.app.Runs.Snapshot(fixture.ctx, fixture.state.ID)
			if err != nil || state.EndReason != reason || state.Status != "closed" {
				subtest.Fatal("incorrect authority outcome", err)
			}
			var amount int64
			if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COALESCE(SUM(amount),0) FROM pve_reward_grants WHERE run_id=?", state.ID).Scan(&amount); err != nil || amount != expected {
				subtest.Fatal("incorrect authority reward", err, amount)
			}
		})
	}
}
