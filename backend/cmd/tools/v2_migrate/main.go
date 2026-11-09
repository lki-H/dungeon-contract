package main

import (
	"context"
	"flag"
	"fmt"
	"log"
	"os"
	"time"

	"game-realtime-gm/backend/internal/config"
	"game-realtime-gm/backend/internal/database"
)

func main() {
	defaultPath := "internal/database/migrations/day37_v2_pve_foundation.sql"
	path := flag.String("migration", defaultPath, "V2 migration file")
	stage := flag.String("stage", "r1", "r1, r2, r3, r4, demo or isolated v3-p0 migration")
	flag.Parse()
	if *stage != "r1" && *stage != "r2" && *stage != "r3" && *stage != "r4" && *stage != "demo" && *stage != "v3-p0" && *stage != "v4-sync" && *stage != "v4-p3" {
		log.Fatal("stage must be r1, r2, r3, r4, demo or v3-p0")
	}
	if *stage == "v3-p0" || *stage == "v4-sync" || *stage == "v4-p3" {
		if os.Getenv("DC_V3_P0_CONFIRM") != "I_UNDERSTAND_DC_V3_P0_MIGRATION" {
			log.Fatal("set DC_V3_P0_CONFIRM=I_UNDERSTAND_DC_V3_P0_MIGRATION before applying the isolated v3 P0 migration")
		}
	} else if os.Getenv("V2_MIGRATION_CONFIRM") != "I_UNDERSTAND_V2_MIGRATION" {
		log.Fatal("set V2_MIGRATION_CONFIRM=I_UNDERSTAND_V2_MIGRATION before applying or reconciling the V2 migration")
	}
	cfg := config.Load()
	if err := config.ValidateDemo(cfg); err != nil {
		log.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Minute)
	defer cancel()
	db, err := database.NewMySQLDB(ctx, cfg.Database)
	if err != nil {
		log.Fatal(err)
	}
	defer db.Close()
	if *stage == "v4-p3" {
		status, err := database.ApplyDemoV4Cooperation(ctx, db, "internal/database/migrations/day46_dc_v4_cooperation.sql")
		if err != nil {
			log.Fatal(err)
		}
		fmt.Println(status)
		return
	}
	if *stage == "v4-sync" {
		status, err := database.ApplyDemoV4Sync(ctx, db, "internal/database/migrations/day45_dc_v4_sync_clock.sql")
		if err != nil {
			log.Fatal(err)
		}
		fmt.Println(status)
		return
	}
	if *stage == "v3-p0" {
		status, err := database.ApplyDemoV3P0(ctx, db, "internal/database/migrations/day44_dc_v3_p0_clock.sql")
		if err != nil {
			log.Fatal(err)
		}
		fmt.Println(status)
		return
	}
	status, err := database.ApplyV2Foundation(ctx, db, *path)
	if err != nil {
		log.Fatal(err)
	}
	fmt.Println(status)
	if *stage == "r2" || *stage == "r3" || *stage == "r4" || *stage == "demo" {
		status, err = database.ApplyV2Products(ctx, db, "internal/database/migrations/day38_v2_product_model.sql")
		if err != nil {
			log.Fatal(err)
		}
		fmt.Println(status)
		if *stage == "r3" || *stage == "r4" || *stage == "demo" {
			status, err = database.ApplyV2Engineering(ctx, db, "internal/database/migrations/day39_v2_engineering.sql")
			if err != nil {
				log.Fatal(err)
			}
			fmt.Println(status)
			if *stage == "r4" || *stage == "demo" {
				status, err = database.ApplyV2Retirement(ctx, db, "internal/database/migrations/day40_v2_retirement.sql")
				if err != nil {
					log.Fatal(err)
				}
				fmt.Println(status)
				if *stage == "demo" {
					status, err = database.ApplyDemoControl(ctx, db, "internal/database/migrations/day41_dc_gameplay_control.sql")
					if err != nil {
						log.Fatal(err)
					}
					fmt.Println(status)
					status, err = database.ApplyDemoResults(ctx, db, "internal/database/migrations/day42_dc_results_trials.sql")
					if err != nil {
						log.Fatal(err)
					}
					fmt.Println(status)
					status, err = database.ApplyDemoQuality(ctx, db, "internal/database/migrations/day43_dc_quality_progress.sql")
					if err != nil {
						log.Fatal(err)
					}
					fmt.Println(status)
				}
			}
		}
	} else if *stage != "r1" {
		log.Fatal("stage must be r1 or r2")
	}
}
