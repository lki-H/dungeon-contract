package config

import (
	"strings"
	"testing"
)

func TestDemoGuardRejectsUnsafeTargetsBeforeConnection(test *testing.T) {
	valid := Config{
		GameplayMode: "v2", AppHost: "127.0.0.1", AppPort: "12880",
		Database:  DatabaseConfig{Host: "127.0.0.1", Port: "23316", Name: "game_realtime_dc_demo", User: "dc_demo", Password: strings.Repeat("a", 64)},
		Redis:     RedisConfig{Addr: "127.0.0.1:26389", Password: strings.Repeat("b", 64)},
		JWTSecret: strings.Repeat("c", 64),
	}
	if err := ValidateDemo(valid); err != nil {
		test.Fatal(err)
	}
	cases := []struct {
		name   string
		mutate func(*Config)
	}{
		{"original database", func(configuration *Config) { configuration.Database.Name = "game_realtime" }},
		{"original MySQL port", func(configuration *Config) { configuration.Database.Port = "3306" }},
		{"root account", func(configuration *Config) { configuration.Database.User = "root" }},
		{"external database", func(configuration *Config) { configuration.Database.Host = "192.168.1.1" }},
		{"original Redis", func(configuration *Config) { configuration.Redis.Addr = "127.0.0.1:6379" }},
		{"shared Redis DB", func(configuration *Config) { configuration.Redis.DB = 1 }},
		{"wildcard HTTP", func(configuration *Config) { configuration.AppHost = "0.0.0.0" }},
		{"original HTTP port", func(configuration *Config) { configuration.AppPort = "8080" }},
		{"legacy writes", func(configuration *Config) { configuration.GameplayMode = "legacy" }},
		{"default password", func(configuration *Config) { configuration.Database.Password = "game123456" }},
		{"invalid secret alphabet", func(configuration *Config) { configuration.JWTSecret = strings.Repeat("z", 64) }},
		{"reused secret", func(configuration *Config) { configuration.Redis.Password = configuration.Database.Password }},
		{"bot source", func(configuration *Config) { configuration.PVE.TestEventsEnabled = true }},
		{"pprof listener", func(configuration *Config) { configuration.Pprof.Enabled = true }},
		{"retention writes", func(configuration *Config) { configuration.PVE.ArchiveEnabled = true }},
		{"external metrics", func(configuration *Config) {
			configuration.PVE.MetricsEnabled = true
			configuration.PVE.MetricsAddr = ":8091"
		}},
	}
	for _, scenario := range cases {
		test.Run(scenario.name, func(subtest *testing.T) {
			configuration := valid
			scenario.mutate(&configuration)
			if err := ValidateDemo(configuration); err == nil {
				subtest.Fatal("unsafe target accepted")
			}
		})
	}
}
