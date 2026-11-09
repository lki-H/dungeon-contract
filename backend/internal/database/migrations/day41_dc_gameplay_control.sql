CREATE TABLE IF NOT EXISTS pve_dc_instances (
    instance_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    service_id VARCHAR(64) NOT NULL,
    boot_id VARCHAR(36) NOT NULL,
    generation BIGINT NOT NULL,
    status VARCHAR(32) NOT NULL,
    lease_until DATETIME(3) NOT NULL,
    endpoint_key VARCHAR(64) NOT NULL,
    reserved_proposal_id VARCHAR(64) NULL,
    reserve_until DATETIME(3) NULL,
    current_assignment_id VARCHAR(64) NULL,
    last_sequence BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (instance_id),
    KEY idx_dc_instance_capacity (status, lease_until),
    KEY idx_dc_instance_reserve (reserve_until)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pve_dc_allocations (
    assignment_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    run_id VARCHAR(64) NOT NULL,
    instance_id VARCHAR(64) NOT NULL,
    boot_id VARCHAR(36) NOT NULL,
    source_generation BIGINT NOT NULL,
    status VARCHAR(32) NOT NULL,
    config_hash CHAR(64) NOT NULL,
    manifest JSON NOT NULL,
    control_issued BIGINT NOT NULL DEFAULT 0,
    control_applied BIGINT NOT NULL DEFAULT 0,
    started_at DATETIME(3) NULL,
    deadline DATETIME(3) NULL,
    seal_deadline DATETIME(3) NULL,
    trial_eligible TINYINT NOT NULL DEFAULT 1,
    state JSON NOT NULL,
    PRIMARY KEY (assignment_id),
    UNIQUE KEY uq_dc_allocation_run (run_id),
    KEY idx_dc_allocation_instance (instance_id, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pve_dc_admissions (
    admission_id VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    operation_id VARCHAR(64) NOT NULL,
    player_id BIGINT NOT NULL,
    run_id VARCHAR(64) NOT NULL,
    assignment_id VARCHAR(64) NOT NULL,
    connection_generation BIGINT NOT NULL,
    session_version BIGINT NOT NULL,
    token_hash CHAR(64) NOT NULL,
    nonce VARCHAR(64) NOT NULL,
    key_version INT NOT NULL,
    expires_at DATETIME(3) NOT NULL,
    consumed_nonce VARCHAR(36) NULL,
    status VARCHAR(32) NOT NULL,
    PRIMARY KEY (admission_id),
    UNIQUE KEY uq_dc_admission_operation (player_id, operation_id),
    UNIQUE KEY uq_dc_admission_hash (token_hash),
    KEY idx_dc_admission_run (run_id, player_id, connection_generation)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pve_dc_control_commands (
    id BIGINT NOT NULL AUTO_INCREMENT,
    run_id VARCHAR(64) NOT NULL,
    control_version BIGINT NOT NULL,
    operation_id VARCHAR(64) NOT NULL,
    command_type VARCHAR(32) NOT NULL,
    player_id BIGINT NULL,
    payload JSON NOT NULL,
    status VARCHAR(32) NOT NULL,
    effective_frame BIGINT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_dc_command_version (run_id, control_version),
    UNIQUE KEY uq_dc_command_operation (run_id, operation_id),
    KEY idx_dc_command_status (run_id, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pve_dc_frame_receipts (
    frame_uuid VARCHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    run_id VARCHAR(64) NOT NULL,
    source_generation BIGINT NOT NULL,
    sequence_no BIGINT NOT NULL,
    world_frame_id BIGINT NOT NULL,
    fingerprint CHAR(64) NOT NULL,
    payload JSON NOT NULL,
    receipt JSON NOT NULL,
    status VARCHAR(32) NOT NULL,
    received_at DATETIME(3) NOT NULL,
    applied_at DATETIME(3) NULL,
    PRIMARY KEY (frame_uuid),
    UNIQUE KEY uq_dc_frame_sequence (run_id, source_generation, sequence_no),
    UNIQUE KEY uq_dc_world_frame (run_id, source_generation, world_frame_id),
    KEY idx_dc_frame_status (run_id, status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pve_dc_ticket_preferences (
    ticket_id VARCHAR(64) NOT NULL,
    mode VARCHAR(16) NOT NULL,
    trial_version VARCHAR(64) NOT NULL,
    PRIMARY KEY (ticket_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
