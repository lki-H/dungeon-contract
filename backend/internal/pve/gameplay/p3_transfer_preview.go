package gameplay

import (
	"context"
	"encoding/json"

	"game-realtime-gm/backend/internal/pve/store"
)

func (service *Service) P3TransferPreview(ctx context.Context) ([]map[string]any, error) {
	if !service.P3() {
		return nil, store.Invalid
	}
	sourceVersion, targetVersion := service.progressTransferVersions()
	rows, err := service.DB.QueryContext(ctx, "SELECT p.player_id,p.task_key,p.progress,p.completed,p.unlocked,EXISTS(SELECT 1 FROM pve_dc_task_transfers t WHERE t.player_id=p.player_id AND t.task_key=p.task_key AND t.source_version=? AND t.target_version=?) FROM pve_player_tasks p WHERE p.version=? ORDER BY p.player_id,p.task_key", sourceVersion, targetVersion, sourceVersion)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	result := []map[string]any{}
	for rows.Next() {
		var player int64
		var key string
		var data []byte
		var complete, unlocked, applied bool
		if err := rows.Scan(&player, &key, &data, &complete, &unlocked, &applied); err != nil {
			return nil, err
		}
		definition, exists := service.Content.Rules.Task(key)
		if !exists {
			continue
		}
		prior := map[string]int64{}
		if err := json.Unmarshal(data, &prior); err != nil {
			return nil, err
		}
		counts := map[string]int64{}
		if !definition.NoKnockdowns && !(service.V5() && key == "dc_precision" && !complete) {
			for _, objective := range definition.Objectives {
				if objective.Persistent {
					counts[objective.Key] = max(0, min(prior[objective.Key], objective.Required))
				}
			}
		}
		result = append(result, map[string]any{"player_id": player, "task": key, "source_version": sourceVersion, "target_version": targetVersion, "source_mapped_counts": counts, "condition_reset": definition.NoKnockdowns, "ambiguous_precision_excluded": service.V5() && key == "dc_precision" && !complete, "preserve_completed": complete, "preserve_unlocked": unlocked, "already_applied": applied, "new_reward": false})
	}
	return result, rows.Err()
}

func (service *Service) progressTransferVersions() (string, string) {
	if service.V5() {
		return "dc_ruins.v4.p3.1", "dc_tasks.v5.p4.1"
	}
	return "dc_ruins.v2", "dc_ruins.v4.p3.1"
}
