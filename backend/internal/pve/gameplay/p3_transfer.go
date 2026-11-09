package gameplay

import (
	"context"
	"database/sql"
	"encoding/json"

	"game-realtime-gm/backend/internal/pve/matchmaking"
	"game-realtime-gm/backend/internal/pve/store"
)

func (service *Service) transferP3Progress(ctx context.Context) error {
	sourceVersion, targetVersion := service.progressTransferVersions()
	return store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		if err := matchmaking.Lane(ctx, transaction); err != nil {
			return err
		}
		var active int
		if err := transaction.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_runs WHERE operation_name='dc_ruins' AND status NOT IN ('closed','aborted')").Scan(&active); err != nil {
			return err
		}
		if active > 0 {
			return ErrProgressTransferBusy
		}
		var pending int
		if err := transaction.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_pending_operations o JOIN pve_runs r ON r.id=o.aggregate_id WHERE r.operation_name='dc_ruins' AND o.status IN ('pending','retryable_failed','needs_repair')").Scan(&pending); err != nil {
			return err
		}
		if pending > 0 {
			return ErrProgressTransferBusy
		}
		rows, err := transaction.QueryContext(ctx, "SELECT p.player_id,p.task_key,p.progress,p.completed,p.unlocked FROM pve_player_tasks p WHERE p.version=? AND NOT EXISTS (SELECT 1 FROM pve_dc_task_transfers t WHERE t.player_id=p.player_id AND t.task_key=p.task_key AND t.source_version=? AND t.target_version=?) ORDER BY p.player_id,p.task_key FOR UPDATE", sourceVersion, sourceVersion, targetVersion)
		if err != nil {
			return err
		}
		type previous struct {
			player              int64
			key                 string
			data                []byte
			completed, unlocked bool
		}
		entries := []previous{}
		for rows.Next() {
			var entry previous
			if err := rows.Scan(&entry.player, &entry.key, &entry.data, &entry.completed, &entry.unlocked); err != nil {
				rows.Close()
				return err
			}
			entries = append(entries, entry)
		}
		err = rows.Err()
		rows.Close()
		if err != nil {
			return err
		}
		for _, entry := range entries {
			definition, exists := service.Content.Rules.Task(entry.key)
			if !exists {
				continue
			}
			prior := map[string]int64{}
			if err := json.Unmarshal(entry.data, &prior); err != nil {
				return err
			}
			counts := map[string]int64{}
			if !definition.NoKnockdowns && !(service.V5() && entry.key == "dc_precision" && !entry.completed) {
				for _, objective := range definition.Objectives {
					if objective.Persistent {
						counts[objective.Key] = max(0, min(prior[objective.Key], objective.Required))
					}
				}
			}
			var targetData []byte
			var targetComplete, targetUnlocked bool
			err := transaction.QueryRowContext(ctx, "SELECT progress,completed,unlocked FROM pve_player_tasks WHERE player_id=? AND task_key=? AND version=? FOR UPDATE", entry.player, entry.key, targetVersion).Scan(&targetData, &targetComplete, &targetUnlocked)
			if err != nil && err != sql.ErrNoRows {
				return err
			}
			if err == nil {
				targetCounts := map[string]int64{}
				if err := json.Unmarshal(targetData, &targetCounts); err != nil {
					return err
				}
				for key, count := range targetCounts {
					counts[key] = max(counts[key], count)
				}
			}
			completed, unlocked := entry.completed || targetComplete, entry.unlocked || targetUnlocked
			if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_player_tasks(player_id,task_key,version,progress,completed,unlocked) VALUES(?,?,?,?,?,?) ON DUPLICATE KEY UPDATE progress=VALUES(progress),completed=VALUES(completed),unlocked=VALUES(unlocked)", entry.player, entry.key, targetVersion, store.JSON(counts), completed, unlocked); err != nil {
				return err
			}
			mapping := store.JSON(map[string]any{"source": sourceVersion, "target": targetVersion, "task": definition, "semantics": "persistent_identical_objective_keys_only_no_knockdown_reset_preserve_once_completion_no_reward", "ambiguous_precision_excluded": service.V5() && entry.key == "dc_precision" && !entry.completed})
			if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_task_transfers(player_id,task_key,source_version,target_version,mapping_hash,source_snapshot,target_snapshot) VALUES(?,?,?,?,?,?,?)", entry.player, entry.key, sourceVersion, targetVersion, store.Hash(mapping), store.JSON(map[string]any{"counts": prior, "completed": entry.completed, "unlocked": entry.unlocked}), store.JSON(map[string]any{"counts": counts, "completed": completed, "unlocked": unlocked})); err != nil {
				return err
			}
			if completed && definition.Next != "" {
				if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_player_tasks(player_id,task_key,version,progress,completed,unlocked) VALUES(?,?,?,'{}',0,1) ON DUPLICATE KEY UPDATE unlocked=1", entry.player, definition.Next, targetVersion); err != nil {
					return err
				}
			}
			if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_task_pauses(player_id,task_key,task_version,paused) SELECT player_id,task_key,?,paused FROM pve_task_pauses WHERE player_id=? AND task_key=? AND task_version=? ON DUPLICATE KEY UPDATE paused=VALUES(paused)", targetVersion, entry.player, entry.key, sourceVersion); err != nil {
				return err
			}
		}
		return nil
	})
}
