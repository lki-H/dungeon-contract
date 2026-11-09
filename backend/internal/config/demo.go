package config

import "errors"

func ValidateDemo(configuration Config) error {
	if configuration.GameplayMode != "v2" || configuration.AppHost != "127.0.0.1" || configuration.AppPort != "12880" {
		return errors.New("demo isolation requires v2 and control listener 127.0.0.1:12880")
	}
	if configuration.Database.Host != "127.0.0.1" || configuration.Database.Port != "23316" || configuration.Database.Name != "game_realtime_dc_demo" || configuration.Database.User != "dc_demo" {
		return errors.New("demo isolation requires the dedicated loopback database and dc_demo account")
	}
	if configuration.Redis.Addr != "127.0.0.1:26389" || configuration.Redis.DB != 0 {
		return errors.New("demo isolation requires the dedicated Redis instance on 127.0.0.1:26389 DB 0")
	}
	secrets := []string{configuration.Database.Password, configuration.Redis.Password, configuration.JWTSecret}
	for index, secret := range secrets {
		if len(secret) != 64 {
			return errors.New("demo isolation requires generated independent 32-byte hex credentials")
		}
		for _, character := range secret {
			if (character < '0' || character > '9') && (character < 'a' || character > 'f') {
				return errors.New("demo isolation requires generated independent 32-byte hex credentials")
			}
		}
		for _, previous := range secrets[:index] {
			if secret == previous {
				return errors.New("demo isolation forbids credential reuse")
			}
		}
	}
	if configuration.PVE.TestEventsEnabled || configuration.Pprof.Enabled || configuration.PVE.ArchiveEnabled {
		return errors.New("demo preparation keeps bot events, pprof and archival writes disabled")
	}
	if configuration.PVE.MetricsEnabled && (configuration.PVE.MetricsAddr != "127.0.0.1:12891" || len(configuration.PVE.MetricsToken) < 32 || configuration.PVE.MetricsToken == configuration.JWTSecret) {
		return errors.New("demo metrics require their dedicated loopback listener and independent credential")
	}
	return nil
}
