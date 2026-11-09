package main

import (
	"context"
	"encoding/json"
	"log"
	"os"
	"time"

	"game-realtime-gm/backend/internal/config"
	"game-realtime-gm/backend/internal/database"
	"game-realtime-gm/backend/internal/pve/gameplay"
)

func main() {
	cfg := config.Load()
	if err := config.ValidateDemo(cfg); err != nil {
		log.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	db, err := database.NewMySQLDB(ctx, cfg.Database)
	if err != nil {
		log.Fatal("Demo storage unavailable")
	}
	defer db.Close()
	content, err := gameplay.LoadContent(os.Getenv("DC_CONFIG_PATH"))
	if err != nil {
		log.Fatal("P3 profile unavailable")
	}
	service := &gameplay.Service{DB: db, Content: content}
	preview, err := service.P3TransferPreview(ctx)
	if err != nil {
		log.Fatal("P3 transfer preview failed")
	}
	if err := json.NewEncoder(os.Stdout).Encode(map[string]any{"dry_run": true, "writes": false, "items": preview}); err != nil {
		log.Fatal("Preview output failed")
	}
}
