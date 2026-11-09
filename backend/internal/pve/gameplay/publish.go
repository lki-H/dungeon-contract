package gameplay

import (
	"context"
	"database/sql"
	"errors"
	"game-realtime-gm/backend/internal/pve/store"
)

func (service *Service) Publish(ctx context.Context) error {
	if service.Content.V3 != nil {
		if service.P3() {
			if err := service.TransferProgress(ctx); err != nil && !errors.Is(err, ErrProgressTransferBusy) {
				return err
			}
		}
		if !service.P5() {
			return nil
		}
	}
	if service.Content.V3 == nil {
		if err := service.TransferProgress(ctx); err != nil && !errors.Is(err, ErrProgressTransferBusy) {
			return err
		}
	}
	return store.Transaction(ctx, service.DB, func(transaction *sql.Tx) error {
		definition := service.Content.Section("trial")
		id, version := text(definition, "id"), text(definition, "version")
		var hash string
		err := transaction.QueryRowContext(ctx, "SELECT config_hash FROM pve_dc_trial_definitions WHERE trial_id=? AND version=? FOR UPDATE", id, version).Scan(&hash)
		if err == nil {
			if hash != service.Content.Hash {
				return store.Conflict
			}
			return nil
		}
		if !errors.Is(err, sql.ErrNoRows) {
			return err
		}
		_, err = transaction.ExecContext(ctx, "INSERT INTO pve_dc_trial_definitions(trial_id,version,status,config_hash,definition_hash,definition) VALUES(?,?,?,?,?,?)", id, version, "published", service.Content.Hash, store.Hash(store.JSON(definition)), store.JSON(definition))
		return err
	})
}
