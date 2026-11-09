package gameplay_test

import (
	"encoding/json"
	"fmt"
	"game-realtime-gm/backend/internal/handler"
	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/store"
	"net/http"
	"net/http/httptest"
	"testing"

	"github.com/gin-gonic/gin"
)

func TestP5StorageEngineeringTrialNeverPublishes(test *testing.T) {
	verifyP5TrialPublication(test, true)
}

func TestP5StorageNormalTrialPublishes(test *testing.T) {
	verifyP5TrialPublication(test, false)
}

func verifyP5TrialPublication(test *testing.T, engineering bool) {
	test.Helper()
	test.Setenv("DC_P5_TEST", "1")
	test.Setenv("DC_P5_TRIAL_TEST", "1")
	engineeringMode := ""
	if engineering {
		engineeringMode = "1"
	}
	test.Setenv("DC_ENGINEERING_RUN", engineeringMode)
	fixture := setup(test, 1)
	fixture.create(test, "")
	fixture.start(test)
	if fixture.manifest.Mode != "trial" || fixture.manifest.TrialVersion != "trial.v5.1" || fixture.manifest.Seed != 20261012 {
		test.Fatal("fixed trial manifest")
	}
	trialVersion := fixture.manifest.TrialVersion
	if !engineering {
		trialVersion = "fixture.p5." + store.ID("trial")
		if _, err := fixture.db.ExecContext(fixture.ctx, "UPDATE pve_dc_allocations SET manifest=JSON_SET(manifest,'$.trial_version',?),state=JSON_SET(state,'$.manifest.trial_version',?) WHERE run_id=?", trialVersion, trialVersion, fixture.state.ID); err != nil {
			test.Fatal(err)
		}
	}
	player := fixture.players[0]
	for index, target := range []string{"hub_a", "hub_b"} {
		stamp := int64(index+1) * 1000
		fixture.p3Send(test, fixture.p3Frame(test, int64(index+1), stamp, p3Fact(player, "interact", target, stamp)))
	}
	kill := p3Fact(player, "kill", "boss", 3000)
	kill.SpawnOrigin = "boss_body"
	frame := fixture.p3Frame(test, 3, 3000, kill)
	frame.Terminal = &gameplay.Terminal{Outcome: "success", Reason: "objectives_complete", Frame: 3, Sequence: 3}
	frame.Summary = &gameplay.Summary{Participants: []gameplay.Stats{{PlayerID: fmt.Sprint(player), Slot: 0, LoadoutID: "ranger", Kills: 1, Interactions: 2, Effective: true, Choices: []string{}}}, Eligible: true, Reason: "none"}
	fixture.p3Send(test, frame)
	fixture.p3Send(test, frame)
	if err := fixture.app.Settlement.Settle(fixture.ctx, fixture.state.ID); err != nil {
		test.Fatal(err)
	}
	if err := fixture.game.PublishClosedTrials(fixture.ctx); err != nil {
		test.Fatal(err)
	}
	var eligible bool
	var status, reason string
	var count int
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT eligible,status,reason FROM pve_dc_trial_records WHERE run_id=?", fixture.state.ID).Scan(&eligible, &status, &reason); err != nil {
		test.Fatal(err)
	}
	if engineering && (eligible || status != "ineligible" || reason != "debug") || !engineering && (!eligible || status != "published") {
		test.Fatal("trial publication policy", engineering, eligible, status, reason)
	}
	if err := fixture.db.QueryRowContext(fixture.ctx, "SELECT COUNT(*) FROM pve_dc_trial_records WHERE run_id=?", fixture.state.ID).Scan(&count); err != nil || count != 1 {
		test.Fatal("duplicate terminal record", count, err)
	}
	cards, err := fixture.game.Contributions(fixture.ctx, fixture.state.ID, player)
	if err != nil || len(cards) != 1 || cards[0]["rescues"] == nil || cards[0]["knockdowns"] == nil || cards[0]["totals_complete"] != true {
		test.Fatal("P5 cooperation cards", cards, err)
	}
	rows, err := fixture.game.Records(fixture.ctx, player, "dc_ruins_trial", trialVersion, 1, 1, 20)
	if err != nil {
		test.Fatal(err)
	}
	expectedTotal := "0"
	if !engineering {
		expectedTotal = "1"
	}
	if fmt.Sprint(rows["total"]) != expectedTotal {
		test.Fatal("trial friend comparison policy", engineering, rows)
	}
	if !engineering {
		release, err := fixture.game.Records(fixture.ctx, player, "dc_ruins_trial", "trial.v5.1", 1, 1, 20)
		if err != nil || fmt.Sprint(release["total"]) != "0" {
			test.Fatal("normal publication fixture leaked into released trial", release, err)
		}
	}
	transport := handler.NewV2Transport(fixture.ctx, fixture.app, "isolated-test-secret")
	defer transport.Close()
	viewer := player
	router := gin.New()
	router.GET("/api/v2/runs/:run_id/results", func(ctx *gin.Context) { ctx.Set("v2_player", viewer) }, transport.Query)
	response := httptest.NewRecorder()
	router.ServeHTTP(response, httptest.NewRequest(http.MethodGet, "/api/v2/runs/"+fixture.state.ID+"/results", nil))
	var result struct {
		Data struct {
			ElapsedMS *int64 `json:"elapsed_ms"`
		} `json:"data"`
	}
	if response.Code != http.StatusOK || json.Unmarshal(response.Body.Bytes(), &result) != nil || result.Data.ElapsedMS == nil || *result.Data.ElapsedMS != 3000 {
		test.Fatal("P5 result authority time missing", response.Code, response.Body.String())
	}
	viewer = player + 99999999
	response = httptest.NewRecorder()
	router.ServeHTTP(response, httptest.NewRequest(http.MethodGet, "/api/v2/runs/"+fixture.state.ID+"/results", nil))
	if response.Code != http.StatusForbidden {
		test.Fatal("nonmember result access", response.Code)
	}
}
