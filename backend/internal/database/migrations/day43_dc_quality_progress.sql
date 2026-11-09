CREATE TABLE IF NOT EXISTS pve_dc_task_transfers (
    player_id BIGINT NOT NULL,
    task_key VARCHAR(64) NOT NULL,
    source_version VARCHAR(64) NOT NULL,
    target_version VARCHAR(64) NOT NULL,
    mapping_hash CHAR(64) NOT NULL,
    source_snapshot JSON NOT NULL,
    target_snapshot JSON NOT NULL,
    transferred_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    PRIMARY KEY (player_id, task_key, source_version, target_version)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
