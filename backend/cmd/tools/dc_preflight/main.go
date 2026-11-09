package main

import (
	"context"
	"crypto/sha256"
	"encoding/json"
	"fmt"
	"log"
	"os"
	"path/filepath"
	"time"

	"game-realtime-gm/backend/internal/cache"
	"game-realtime-gm/backend/internal/config"
	"game-realtime-gm/backend/internal/database"
)

func main() {
	configuration := config.Load()
	if err := config.ValidateDemo(configuration); err != nil {
		log.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	db, err := database.NewMySQLDB(ctx, configuration.Database)
	if err != nil {
		log.Fatal("Demo MySQL readiness failed")
	}
	defer db.Close()
	var databaseName string
	if err := db.QueryRowContext(ctx, "SELECT DATABASE()").Scan(&databaseName); err != nil || databaseName != "game_realtime_dc_demo" {
		log.Fatal("Demo database identity mismatch")
	}
	rows, err := db.QueryContext(ctx, "SELECT migration_id,checksum,status FROM schema_migrations")
	if err != nil {
		log.Fatal("Demo migration records are unavailable")
	}
	records := make([]database.MigrationRecord, 0, 8)
	for rows.Next() {
		var record database.MigrationRecord
		if err := rows.Scan(&record.ID, &record.Checksum, &record.Status); err != nil {
			rows.Close()
			log.Fatal("Demo migration record is unreadable")
		}
		records = append(records, record)
	}
	if err := rows.Err(); err != nil {
		rows.Close()
		log.Fatal("Demo migration record query failed")
	}
	rows.Close()
	if err := validateMigrationRecords(records, "internal/database/migrations"); err != nil {
		log.Fatal(err)
	}
	redisClient, err := cache.NewRedisClient(ctx, configuration.Redis)
	if err != nil {
		log.Fatal("Demo authenticated Redis readiness failed")
	}
	defer redisClient.Close()
	demoMigrations := len(records) - 4
	v3P0Migrations := 0
	v4SyncMigrations := 0
	v4CooperationMigrations := 0
	if len(records) >= 8 {
		demoMigrations = 3
		v3P0Migrations = 1
	}
	if len(records) >= 9 {
		v4SyncMigrations = 1
	}
	if len(records) == 10 {
		v4CooperationMigrations = 1
	}
	if err := json.NewEncoder(os.Stdout).Encode(map[string]any{
		"database": databaseName, "inherited_migrations": 4, "demo_migrations": demoMigrations, "v3_p0_migrations": v3P0Migrations, "v4_sync_migrations": v4SyncMigrations, "v4_cooperation_migrations": v4CooperationMigrations, "dirty_migrations": 0,
		"authenticated_redis": true,
	}); err != nil {
		log.Fatal("write preflight result failed")
	}
}

var demoMigrationChain = []string{
	"day37_v2_pve_foundation", "day38_v2_product_model", "day39_v2_engineering", "day40_v2_retirement",
	"day41_dc_gameplay_control", "day42_dc_results_trials", "day43_dc_quality_progress", "day44_dc_v3_p0_clock",
	"day45_dc_v4_sync_clock",
	"day46_dc_v4_cooperation",
}

func validateMigrationRecords(records []database.MigrationRecord, directory string) error {
	if len(records) != 4 && len(records) != 6 && len(records) != 7 && len(records) != 8 && len(records) != 9 && len(records) != 10 {
		return fmt.Errorf("Demo migration chain is incomplete")
	}
	byID := make(map[string]database.MigrationRecord, len(records))
	for _, record := range records {
		if _, exists := byID[record.ID]; exists {
			return fmt.Errorf("Demo migration record is duplicated: %s", record.ID)
		}
		byID[record.ID] = record
	}
	for _, migrationID := range demoMigrationChain[:len(records)] {
		record, exists := byID[migrationID]
		if !exists || (record.Status != "applied" && record.Status != "reconciled") {
			return fmt.Errorf("Demo migration is missing or requires repair: %s", migrationID)
		}
		data, err := os.ReadFile(filepath.Join(directory, migrationID+".sql"))
		if err != nil {
			return fmt.Errorf("Demo migration source is unavailable: %s", migrationID)
		}
		if record.Checksum != fmt.Sprintf("%x", sha256.Sum256(data)) {
			return fmt.Errorf("Demo migration checksum mismatch: %s", migrationID)
		}
	}
	return nil
}
