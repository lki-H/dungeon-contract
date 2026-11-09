package main

import (
	"crypto/sha256"
	"fmt"
	"os"
	"path/filepath"
	"testing"

	"game-realtime-gm/backend/internal/database"
)

func TestMigrationPreflightChecksIdentityStatusAndChecksum(test *testing.T) {
	directory := filepath.Join("..", "..", "..", "internal", "database", "migrations")
	records := make([]database.MigrationRecord, 0, 8)
	for _, migrationID := range demoMigrationChain {
		data, err := os.ReadFile(filepath.Join(directory, migrationID+".sql"))
		if err != nil {
			test.Fatal(err)
		}
		records = append(records, database.MigrationRecord{ID: migrationID, Checksum: fmt.Sprintf("%x", sha256.Sum256(data)), Status: "applied"})
	}
	for _, count := range []int{4, 6, 7, 8, 9, 10} {
		if err := validateMigrationRecords(records[:count], directory); err != nil {
			test.Fatal(err)
		}
	}
	for _, scenario := range []struct {
		name   string
		mutate func([]database.MigrationRecord)
	}{
		{"same count wrong identity", func(records []database.MigrationRecord) { records[0].ID = "unknown_migration" }},
		{"dirty status", func(records []database.MigrationRecord) { records[7].Status = "failed" }},
		{"changed checksum", func(records []database.MigrationRecord) { records[7].Checksum = "wrong" }},
		{"duplicate identity", func(records []database.MigrationRecord) { records[7].ID = records[0].ID }},
		{"day45 dirty status", func(records []database.MigrationRecord) { records[8].Status = "failed" }},
		{"day45 wrong checksum", func(records []database.MigrationRecord) { records[8].Checksum = "wrong" }},
		{"day46 dirty status", func(records []database.MigrationRecord) { records[9].Status = "failed" }},
		{"day46 wrong checksum", func(records []database.MigrationRecord) { records[9].Checksum = "wrong" }},
	} {
		test.Run(scenario.name, func(test *testing.T) {
			modified := append([]database.MigrationRecord(nil), records...)
			scenario.mutate(modified)
			if err := validateMigrationRecords(modified, directory); err == nil {
				test.Fatal("invalid migration chain accepted")
			}
		})
	}
}
