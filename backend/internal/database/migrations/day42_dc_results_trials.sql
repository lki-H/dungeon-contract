CREATE TABLE IF NOT EXISTS pve_dc_participant_stats (
    run_id VARCHAR(64) NOT NULL,
    player_id BIGINT NOT NULL,
    damage BIGINT NOT NULL DEFAULT 0,
    kills BIGINT NOT NULL DEFAULT 0,
    assists BIGINT NOT NULL DEFAULT 0,
    interactions BIGINT NOT NULL DEFAULT 0,
    deaths BIGINT NOT NULL DEFAULT 0,
    last_sequence BIGINT NOT NULL DEFAULT 0,
    loadout VARCHAR(32) NOT NULL,
    choices JSON NOT NULL,
    effective_contribution TINYINT NOT NULL DEFAULT 0,
    final_hash CHAR(64) NULL,
    PRIMARY KEY (run_id, player_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pve_dc_trial_definitions (
    trial_id VARCHAR(64) NOT NULL,
    version VARCHAR(64) NOT NULL,
    status VARCHAR(16) NOT NULL,
    config_hash CHAR(64) NOT NULL,
    definition_hash CHAR(64) NOT NULL,
    definition JSON NOT NULL,
    PRIMARY KEY (trial_id, version)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pve_dc_trial_records (
    record_id VARCHAR(64) NOT NULL,
    run_id VARCHAR(64) NOT NULL,
    trial_id VARCHAR(64) NOT NULL,
    trial_version VARCHAR(64) NOT NULL,
    initial_player_count INT NOT NULL,
    elapsed_ms BIGINT NOT NULL,
    completed_at DATETIME(3) NOT NULL,
    eligible TINYINT NOT NULL,
    status VARCHAR(16) NOT NULL,
    summary_hash CHAR(64) NOT NULL,
    reason VARCHAR(64) NOT NULL,
    PRIMARY KEY (record_id),
    UNIQUE KEY uq_dc_trial_run (run_id),
    KEY idx_dc_trial_bucket (trial_id, trial_version, initial_player_count, elapsed_ms, completed_at, record_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pve_dc_trial_record_members (
    record_id VARCHAR(64) NOT NULL,
    player_id BIGINT NOT NULL,
    slot INT NOT NULL,
    loadout_id VARCHAR(32) NOT NULL,
    PRIMARY KEY (record_id, player_id),
    UNIQUE KEY uq_dc_trial_slot (record_id, slot),
    KEY idx_dc_trial_player (player_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
