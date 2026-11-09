package gameplay

import (
	"context"
	"database/sql"
	"strconv"

	"game-realtime-gm/backend/internal/pve/run"
	"game-realtime-gm/backend/internal/pve/store"
)

func (service *Service) persistP3Prefix(ctx context.Context, transaction *sql.Tx, state *run.State, allocation *Allocation) error {
	if allocation.P3FinalSummary {
		return nil
	}
	for _, member := range state.Participants {
		id := strconv.FormatInt(member.PlayerID, 10)
		stats := allocation.Stats[id]
		stats.Deaths = member.Deaths
		stats.Knockdowns = member.Knockdowns
		stats.Rescues = member.Rescues
		stats.ProtectedDamage = member.ProtectedDamage
		stats.ControlEffect = member.ControlEffect
		stats.ClearedProjectiles = member.ClearedProjectiles
		stats.Effective = member.Contribution > 0
		if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_participant_stats(run_id,player_id,damage,kills,assists,interactions,deaths,last_sequence,loadout,choices,effective_contribution,final_hash) VALUES(?,?,?,?,?,?,?,?,?,?,?,?) ON DUPLICATE KEY UPDATE damage=VALUES(damage),kills=VALUES(kills),assists=VALUES(assists),interactions=VALUES(interactions),deaths=VALUES(deaths),last_sequence=VALUES(last_sequence),effective_contribution=VALUES(effective_contribution),final_hash=VALUES(final_hash)", state.ID, member.PlayerID, stats.Damage, stats.Kills, stats.Assists, stats.Interactions, stats.Deaths, state.AppliedSequence, stats.LoadoutID, store.JSON(stats.Choices), stats.Effective, store.Hash(store.JSON(map[string]any{"source": "confirmed_prefix_only", "stats": stats}))); err != nil {
			return err
		}
		if _, err := transaction.ExecContext(ctx, "INSERT INTO pve_dc_v4_life_stats(run_id,player_id,slot,knockdowns,rescues,protected_damage,control_effect_ms) VALUES(?,?,?,?,?,?,?) ON DUPLICATE KEY UPDATE knockdowns=VALUES(knockdowns),rescues=VALUES(rescues),protected_damage=VALUES(protected_damage),control_effect_ms=VALUES(control_effect_ms)", state.ID, member.PlayerID, stats.Slot, stats.Knockdowns, stats.Rescues, stats.ProtectedDamage, stats.ControlEffect); err != nil {
			return err
		}
	}
	return nil
}

func (service *Service) RecoverP3Summaries(ctx context.Context) error {
	if !service.P3() {
		return nil
	}
	rows, err := service.DB.QueryContext(ctx, "SELECT r.id FROM pve_runs r JOIN pve_dc_allocations a ON a.run_id=r.id WHERE r.rule_version=? AND r.status IN ('ending','closed') AND NOT EXISTS(SELECT 1 FROM pve_dc_v4_life_stats s WHERE s.run_id=r.id)", service.Content.Rules.Version)
	if err != nil {
		return err
	}
	ids := []string{}
	for rows.Next() {
		var id string
		if err := rows.Scan(&id); err != nil {
			rows.Close()
			return err
		}
		ids = append(ids, id)
	}
	err = rows.Err()
	rows.Close()
	if err != nil {
		return err
	}
	for _, id := range ids {
		if err := store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
			state, allocation, err := service.load(ctx, transaction, id)
			if err != nil {
				return err
			}
			return service.persistP3Prefix(ctx, transaction, state, allocation)
		}); err != nil {
			return err
		}
	}
	return nil
}
