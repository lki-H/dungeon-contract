package gameplay

import (
	"context"
	"encoding/json"
	"fmt"
	"sort"

	"game-realtime-gm/backend/internal/pve/run"
	"game-realtime-gm/backend/internal/pve/social"
	"game-realtime-gm/backend/internal/pve/store"
)

func (service *Service) Trials(ctx context.Context, page, size int) (map[string]any, error) {
	if page < 1 || page > 10000 || size < 1 || size > 50 {
		return nil, store.Invalid
	}
	var total int
	if err := service.DB.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_dc_trial_definitions WHERE status='published'").Scan(&total); err != nil {
		return nil, err
	}
	rows, err := service.DB.QueryContext(ctx, "SELECT trial_id AS id,version,config_hash,status,definition FROM pve_dc_trial_definitions WHERE status='published' ORDER BY trial_id,version LIMIT ? OFFSET ?", size, (page-1)*size)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	items, err := social.Rows(rows)
	return map[string]any{"items": items, "page": page, "page_size": size, "total": total}, err
}

func (service *Service) Records(ctx context.Context, player int64, trial, version string, count, page, size int) (map[string]any, error) {
	if player <= 0 || page < 1 || page > 10000 || size < 1 || size > 50 || trial != "" && (trial != "dc_ruins_trial" || version == "" || len(version) > 64 || count < 1 || count > 4) {
		return nil, store.Invalid
	}
	if trial == "" {
		return service.ownRecords(ctx, player, page, size)
	}
	condition := "m.player_id=?"
	arguments := []any{player}
	if trial != "" {
		condition = "r.trial_id=? AND r.trial_version=? AND r.initial_player_count=? AND r.eligible=1 AND r.status='published' AND (m.player_id=? OR EXISTS(SELECT 1 FROM pve_social_friendships f WHERE f.status='active' AND f.player_low_id=LEAST(m.player_id,?) AND f.player_high_id=GREATEST(m.player_id,?)))"
		arguments = []any{trial, version, count, player, player, player}
	}
	var total int
	if err := service.DB.QueryRowContext(ctx, "SELECT COUNT(DISTINCT r.record_id) FROM pve_dc_trial_records r JOIN pve_dc_trial_record_members m ON m.record_id=r.record_id WHERE "+condition, arguments...).Scan(&total); err != nil {
		return nil, err
	}
	queryArguments := append(append([]any{}, arguments...), size, (page-1)*size)
	names := "(SELECT GROUP_CONCAT(p.nickname ORDER BY rm.slot SEPARATOR ' / ') FROM pve_dc_trial_record_members rm JOIN players p ON p.id=rm.player_id WHERE rm.record_id=r.record_id AND (rm.player_id=? OR EXISTS(SELECT 1 FROM pve_social_friendships f WHERE f.status='active' AND f.player_low_id=LEAST(rm.player_id,?) AND f.player_high_id=GREATEST(rm.player_id,?)))) AS visible_names"
	queryArguments = append([]any{player, player, player}, queryArguments...)
	rows, err := service.DB.QueryContext(ctx, "SELECT DISTINCT r.record_id,r.run_id,r.trial_id,r.trial_version,r.initial_player_count,r.elapsed_ms,r.completed_at,r.eligible,r.status,r.reason,"+names+" FROM pve_dc_trial_records r JOIN pve_dc_trial_record_members m ON m.record_id=r.record_id WHERE "+condition+" ORDER BY r.elapsed_ms,r.completed_at,r.record_id LIMIT ? OFFSET ?", queryArguments...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	items, err := social.Rows(rows)
	return map[string]any{"items": items, "page": page, "page_size": size, "total": total}, err
}

func (service *Service) Contributions(ctx context.Context, runID string, player int64) ([]map[string]any, error) {
	var member int
	if err := service.DB.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_run_participants WHERE run_id=? AND player_id=?", runID, player).Scan(&member); err != nil {
		return nil, err
	}
	if member == 0 {
		return nil, store.Forbidden
	}
	var ruleVersion string
	if err := service.DB.QueryRowContext(ctx, "SELECT rule_version FROM pve_runs WHERE id=?", runID).Scan(&ruleVersion); err != nil {
		return nil, err
	}
	p3Record := run.CooperationVersion(ruleVersion)
	query := "SELECT s.player_id,p.nickname,s.loadout AS loadout_id,s.damage AS effective_damage,s.kills AS eligible_kills,s.assists,s.interactions,s.deaths FROM pve_dc_participant_stats s JOIN players p ON p.id=s.player_id JOIN pve_run_participants m ON m.run_id=s.run_id AND m.player_id=s.player_id WHERE s.run_id=? ORDER BY m.participant_id LIMIT 4"
	if p3Record {
		query = "SELECT s.player_id,p.nickname,s.loadout AS loadout_id,s.damage AS effective_damage,s.kills AS eligible_kills,s.assists,s.interactions,s.deaths,l.slot,l.knockdowns,l.rescues,l.protected_damage,l.control_effect_ms FROM pve_dc_participant_stats s JOIN players p ON p.id=s.player_id JOIN pve_dc_v4_life_stats l ON l.run_id=s.run_id AND l.player_id=s.player_id WHERE s.run_id=? ORDER BY l.slot LIMIT 4"
	}
	rows, err := service.DB.QueryContext(ctx, query, runID)
	if err != nil {
		return nil, err
	}
	cards, err := social.Rows(rows)
	rows.Close()
	if err != nil {
		return nil, err
	}
	var data []byte
	if err := service.DB.QueryRowContext(ctx, "SELECT manifest FROM pve_dc_allocations WHERE run_id=?", runID).Scan(&data); err != nil {
		return nil, err
	}
	var manifest Manifest
	if err := json.Unmarshal(data, &manifest); err != nil {
		return nil, err
	}
	slots := map[string]int{}
	for _, member := range manifest.Members {
		slots[member.PlayerID] = member.Slot
	}
	for _, card := range cards {
		card["slot"] = slots[fmt.Sprint(card["player_id"])]
	}
	if p3Record {
		for _, card := range cards {
			card["kills"] = card["eligible_kills"]
			delete(card, "eligible_kills")
		}
	}
	if p3Record {
		var data []byte
		if err := service.DB.QueryRowContext(ctx, "SELECT state FROM pve_dc_allocations WHERE run_id=?", runID).Scan(&data); err != nil {
			return nil, err
		}
		var allocation Allocation
		if err := json.Unmarshal(data, &allocation); err != nil {
			return nil, err
		}
		for _, card := range cards {
			card["totals_complete"] = allocation.P3FinalSummary
			card["cleared_projectiles"] = allocation.Stats[fmt.Sprint(card["player_id"])].ClearedProjectiles
		}
	}
	sort.SliceStable(cards, func(first, second int) bool {
		return slots[fmt.Sprint(cards[first]["player_id"])] < slots[fmt.Sprint(cards[second]["player_id"])]
	})
	return cards, nil
}

func (service *Service) PublishClosedTrials(ctx context.Context) error {
	_, err := service.DB.ExecContext(ctx, "UPDATE pve_dc_trial_records t JOIN pve_runs r ON r.id=t.run_id SET t.status=IF(t.eligible=1 AND r.end_reason='success','published','ineligible'),t.eligible=IF(r.end_reason='success',t.eligible,0) WHERE t.status='pending' AND r.status='closed'")
	return err
}

func (service *Service) ownRecords(ctx context.Context, player int64, page, size int) (map[string]any, error) {
	var total int
	if err := service.DB.QueryRowContext(ctx, "SELECT COUNT(*) FROM pve_dc_allocations a JOIN pve_run_participants m ON m.run_id=a.run_id WHERE m.player_id=?", player).Scan(&total); err != nil {
		return nil, err
	}
	rows, err := service.DB.QueryContext(ctx, "SELECT r.id AS run_id,r.status,r.end_reason AS reason,JSON_UNQUOTE(JSON_EXTRACT(a.manifest,'$.mode')) AS mode,JSON_EXTRACT(a.state,'$.elapsed_ms') AS elapsed_ms,r.rule_version,s.loadout AS own_loadout,p.result_status AS own_reward_status,t.eligible AS trial_eligible FROM pve_runs r JOIN pve_dc_allocations a ON a.run_id=r.id JOIN pve_run_participants m ON m.run_id=r.id LEFT JOIN pve_dc_participant_stats s ON s.run_id=r.id AND s.player_id=m.player_id LEFT JOIN pve_participant_results p ON p.run_id=r.id AND p.player_id=m.player_id LEFT JOIN pve_dc_trial_records t ON t.run_id=r.id WHERE m.player_id=? ORDER BY r.created_at DESC,r.id LIMIT ? OFFSET ?", player, size, (page-1)*size)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	items, err := social.Rows(rows)
	return map[string]any{"items": items, "page": page, "page_size": size, "total": total}, err
}
