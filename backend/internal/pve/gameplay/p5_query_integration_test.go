package gameplay_test

import (
	"fmt"
	"game-realtime-gm/backend/internal/pve/store"
	"strings"
	"testing"
)

func TestP5StorageFriendRecordNamesAreCropped(test *testing.T) {
	test.Setenv("DC_P5_TEST", "1")
	test.Setenv("DC_ENGINEERING_RUN", "1")
	fixture := setup(test, 4)
	fixture.create(test, "")
	names := []string{"P5VisibleOwner", "P5VisibleFriend", "P5HiddenA", "P5HiddenB"}
	for index, player := range fixture.players {
		if _, err := fixture.db.ExecContext(fixture.ctx, "UPDATE players SET nickname=? WHERE id=?", names[index], player); err != nil {
			test.Fatal(err)
		}
	}
	if _, err := fixture.db.ExecContext(fixture.ctx, "INSERT INTO pve_social_friendships(player_low_id,player_high_id,status) VALUES(LEAST(?,?),GREATEST(?,?),'active')", fixture.players[0], fixture.players[1], fixture.players[0], fixture.players[1]); err != nil {
		test.Fatal(err)
	}
	version := "query-fixture." + store.ID("v")
	record := store.ID("record")
	if _, err := fixture.db.ExecContext(fixture.ctx, "INSERT INTO pve_dc_trial_records(record_id,run_id,trial_id,trial_version,initial_player_count,elapsed_ms,completed_at,eligible,status,summary_hash,reason) VALUES(?,?,'dc_ruins_trial',?,4,12345,UTC_TIMESTAMP(3),1,'published',?,'isolated_query_fixture')", record, fixture.state.ID, version, store.Hash([]byte("isolated_query_fixture"))); err != nil {
		test.Fatal(err)
	}
	for index, player := range fixture.players {
		if _, err := fixture.db.ExecContext(fixture.ctx, "INSERT INTO pve_dc_trial_record_members(record_id,player_id,slot,loadout_id) VALUES(?,?,?,'ranger')", record, player, index); err != nil {
			test.Fatal(err)
		}
	}
	result, err := fixture.game.Records(fixture.ctx, fixture.players[0], "dc_ruins_trial", version, 4, 1, 20)
	if err != nil {
		test.Fatal(err)
	}
	rows := result["items"].([]map[string]any)
	if len(rows) != 1 {
		test.Fatal("missing isolated query fixture", result)
	}
	visible := fmt.Sprint(rows[0]["visible_names"])
	if !strings.Contains(visible, names[0]) || !strings.Contains(visible, names[1]) || strings.Contains(visible, names[2]) || strings.Contains(visible, names[3]) {
		test.Fatal("record name privacy", visible)
	}
	own, err := fixture.game.Records(fixture.ctx, fixture.players[3], "dc_ruins_trial", version, 4, 1, 20)
	if err != nil {
		test.Fatal(err)
	}
	if fmt.Sprint(own["items"].([]map[string]any)[0]["visible_names"]) != names[3] {
		test.Fatal("unrelated teammates disclosed")
	}
	release, err := fixture.game.Records(fixture.ctx, fixture.players[0], "dc_ruins_trial", "trial.v5.1", 4, 1, 20)
	if err != nil {
		test.Fatal(err)
	}
	if fmt.Sprint(release["total"]) != "0" {
		test.Fatal("query fixture leaked into released trial")
	}
}
