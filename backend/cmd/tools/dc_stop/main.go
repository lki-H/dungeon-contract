package main

import (
	"context"
	"fmt"
	"log"
	"time"

	"game-realtime-gm/backend/internal/config"
	"game-realtime-gm/backend/internal/database"
	"game-realtime-gm/backend/internal/pve/gameplay"
	"game-realtime-gm/backend/internal/pve/settlement"
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
		log.Fatal("Demo storage unavailable; active results were not changed")
	}
	defer db.Close()
	rows, err := db.QueryContext(ctx, "SELECT run_id,source_generation FROM pve_dc_allocations WHERE status IN ('loading','running','draining') LIMIT 100")
	if err != nil {
		log.Fatal(err)
	}
	type item struct {
		id         string
		generation int64
	}
	items := []item{}
	for rows.Next() {
		var value item
		if err := rows.Scan(&value.id, &value.generation); err != nil {
			rows.Close()
			log.Fatal(err)
		}
		items = append(items, value)
	}
	err = rows.Err()
	rows.Close()
	if err != nil {
		log.Fatal(err)
	}
	service := &gameplay.Service{DB: db, Now: time.Now}
	settler := settlement.NewService(db)
	for _, value := range items {
		if err := service.Abort(ctx, value.id, value.generation); err != nil {
			log.Fatal("Demo abort requires repair")
		}
		if err := settler.Settle(ctx, value.id); err != nil {
			log.Fatal("Demo result remains pending; credentials and data retained")
		}
	}
	fmt.Printf("Demo shutdown resolved %d authority results; no volume or reward history deleted.\n", len(items))
}
