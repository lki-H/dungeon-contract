CREATE TABLE IF NOT EXISTS pve_dc_v4_life_stats (
    run_id VARCHAR(64) NOT NULL,
    player_id BIGINT NOT NULL,
    slot INT NOT NULL,
    knockdowns BIGINT NOT NULL,
    rescues BIGINT NOT NULL,
    protected_damage BIGINT NOT NULL,
    control_effect_ms BIGINT NOT NULL,
    PRIMARY KEY (run_id, player_id),
    UNIQUE KEY uq_dc_v4_life_slot (run_id, slot),
    CONSTRAINT chk_dc_v4_life_stats CHECK (slot >= 0 AND slot <= 3 AND knockdowns >= 0 AND rescues >= 0 AND protected_damage >= 0 AND control_effect_ms >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
