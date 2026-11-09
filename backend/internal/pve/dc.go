package pve

import (
	"context"
	"database/sql"
	"errors"
	"os"
	"time"

	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/party"
	"game-realtime-gm/backend/internal/pve/store"
)

func (app *App) EnableGame(content *gameplay.Content, key []byte) error {
	if len(key) != 32 {
		return store.Invalid
	}
	game := &gameplay.Service{DB: app.DB, Runs: app.Runs, Content: content, Key: key, ServiceID: "dc_local_service", InstanceID: "dc_local_01", Now: time.Now}
	game.Engineering = os.Getenv("DC_ENGINEERING_RUN") == "1"
	app.Game = game
	app.Runs.Rules = content.Rules
	app.Match.ReserveProposal = game.ReserveTx
	app.Match.ReleaseProposal = game.ReleaseTx
	app.Match.Allocate = game.AllocateTx
	game.CapacityFailed = app.Match.AbortCapacity
	app.Match.SetPreferences = func(ctx context.Context, transaction *sql.Tx, ticket string, plan party.Plan) error {
		mode := plan.Mode
		if mode == "" {
			mode = "normal"
		}
		if mode != "normal" && mode != "trial" {
			return store.Invalid
		}
		if content.V3 != nil && mode != "normal" && !game.P5() {
			return store.Invalid
		}
		if plan.Operation == content.Rules.Operation && plan.RuleVersion != content.Rules.Version {
			return store.Invalid
		}
		if content.Rules.Version == "dc_ruins.v3.p1" && plan.FillPolicy != "no_fill" {
			return store.Invalid
		}
		if mode == "trial" && (plan.Operation != content.Rules.Operation || plan.TrialID != content.Section("trial")["id"] || plan.TrialVersion != content.Section("trial")["version"]) {
			return store.Invalid
		}
		if mode == "normal" && (plan.TrialID != "" || plan.TrialVersion != "") {
			return store.Invalid
		}
		_, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_ticket_preferences(ticket_id,mode,trial_version) VALUES(?,?,?) ON DUPLICATE KEY UPDATE mode=VALUES(mode),trial_version=VALUES(trial_version)", ticket, mode, plan.TrialVersion)
		return err
	}
	app.Match.GetPreferences = func(ctx context.Context, transaction *sql.Tx, ticket string) (string, string, error) {
		var mode, version string
		err := transaction.QueryRowContext(ctx, "SELECT mode,trial_version FROM pve_dc_ticket_preferences WHERE ticket_id=?", ticket).Scan(&mode, &version)
		if errors.Is(err, sql.ErrNoRows) {
			return "normal", "", nil
		}
		return mode, version, err
	}
	return nil
}
