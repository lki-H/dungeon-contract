package gameplay

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"

	"game-realtime-gm/backend/internal/pve/store"
)

var ErrProgressTransferBusy = errors.New("progress transfer requires drained runs")

func (service *Service) TransferProgress(ctx context.Context) error {
	if service.P3() {
		return service.transferP3Progress(ctx)
	}
	if service.Content.Rules.Version != "dc_ruins.v2" {
		return nil
	}
	return store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		rows, err := transaction.QueryContext(ctx, "SELECT player_id,task_key,progress,completed,unlocked FROM pve_player_tasks p WHERE version='dc_ruins.v1' AND NOT EXISTS (SELECT 1 FROM pve_dc_task_transfers t WHERE t.player_id=p.player_id AND t.task_key=p.task_key AND t.source_version='dc_ruins.v1' AND t.target_version='dc_ruins.v2') ORDER BY player_id,task_key FOR UPDATE")
		if err != nil {
			return err
		}
		type priorProgress struct {
			player              int64
			key                 string
			data                []byte
			completed, unlocked bool
		}
		prior := []priorProgress{}
		for rows.Next() {
			var entry priorProgress
			if err := rows.Scan(&entry.player, &entry.key, &entry.data, &entry.completed, &entry.unlocked); err != nil {
				rows.Close()
				return err
			}
			prior = append(prior, entry)
		}
		if err := rows.Err(); err != nil {
			rows.Close()
			return err
		}
		rows.Close()
		if len(prior) == 0 {
			return nil
		}
		var pending int
		if err := transaction.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_runs WHERE operation_name='dc_ruins' AND status NOT IN ('closed','aborted')").Scan(&pending); err != nil {
			return err
		}
		if pending != 0 {
			return ErrProgressTransferBusy
		}
		for _, entry := range prior {
			definition, exists := service.Content.Rules.Task(entry.key)
			if !exists {
				continue
			}
			var transferred int
			if err := transaction.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_dc_task_transfers WHERE player_id=? AND task_key=? AND source_version='dc_ruins.v1' AND target_version='dc_ruins.v2'", entry.player, entry.key).Scan(&transferred); err != nil {
				return err
			}
			if transferred != 0 {
				continue
			}
			oldCounts := map[string]int64{}
			if err := json.Unmarshal(entry.data, &oldCounts); err != nil {
				return err
			}
			counts := map[string]int64{}
			for _, objective := range definition.Objectives {
				if objective.Persistent {
					counts[objective.Key] = min(oldCounts[objective.Key], objective.Required)
				}
			}
			mapping := store.JSON(map[string]any{"source": "dc_ruins.v1", "target": "dc_ruins.v2", "key": entry.key, "persistent_objective_keys": definition.Objectives})
			target := store.JSON(map[string]any{"counts": counts, "completed": entry.completed, "unlocked": entry.unlocked})
			if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_player_tasks(player_id,task_key,version,progress,completed,unlocked) VALUES(?,?,'dc_ruins.v2',?,?,?) ON DUPLICATE KEY UPDATE completed=GREATEST(completed,VALUES(completed)),unlocked=GREATEST(unlocked,VALUES(unlocked))", entry.player, entry.key, store.JSON(counts), entry.completed, entry.unlocked); err != nil {
				return err
			}
			if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_task_transfers(player_id,task_key,source_version,target_version,mapping_hash,source_snapshot,target_snapshot) VALUES(?,?,'dc_ruins.v1','dc_ruins.v2',?,?,?)", entry.player, entry.key, store.Hash(mapping), store.JSON(map[string]any{"counts": oldCounts, "completed": entry.completed, "unlocked": entry.unlocked}), target); err != nil {
				return err
			}
			if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_task_pauses(player_id,task_key,task_version,paused) SELECT player_id,task_key,'dc_ruins.v2',paused FROM pve_task_pauses WHERE player_id=? AND task_key=? AND task_version='dc_ruins.v1' ON DUPLICATE KEY UPDATE paused=VALUES(paused)", entry.player, entry.key); err != nil {
				return err
			}
		}
		return nil
	})
}
