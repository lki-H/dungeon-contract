CREATE TABLE IF NOT EXISTS pve_dc_v4_run_clocks (
    run_id VARCHAR(64) NOT NULL,
    clock_state VARCHAR(32) NOT NULL,
    clock_revision BIGINT NOT NULL,
    game_elapsed_ms BIGINT NOT NULL,
    wall_elapsed_ms BIGINT NOT NULL,
    manual_pause_ms BIGINT NOT NULL,
    PRIMARY KEY (run_id),
    CONSTRAINT chk_dc_v4_clock CHECK (clock_revision > 0 AND game_elapsed_ms >= 0 AND manual_pause_ms >= 0 AND wall_elapsed_ms = game_elapsed_ms + manual_pause_ms AND clock_state IN ('running','paused','technical_wait'))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pve_dc_v4_pause_intervals (
    run_id VARCHAR(64) NOT NULL,
    pause_id VARCHAR(64) NOT NULL,
    begin_operation VARCHAR(64) NOT NULL,
    begin_wall_ms BIGINT NOT NULL,
    begin_game_ms BIGINT NOT NULL,
    end_operation VARCHAR(64) NULL,
    end_wall_ms BIGINT NULL,
    status VARCHAR(16) NOT NULL,
    PRIMARY KEY (run_id,pause_id),
    UNIQUE KEY uq_dc_v4_pause_operation (run_id,begin_operation)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pve_dc_v4_clock_receipts (
    run_id VARCHAR(64) NOT NULL,
    operation_id VARCHAR(64) NOT NULL,
    request_hash CHAR(64) NOT NULL,
    receipt JSON NOT NULL,
    PRIMARY KEY (run_id,operation_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
