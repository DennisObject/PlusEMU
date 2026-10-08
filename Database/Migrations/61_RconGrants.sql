-- Receipts of applied RCON grant bundles, one per CMS idempotency key. A retry with the same key and payload replays the
-- stored result instead of granting again. Only applied grants are recorded, so a refused grant leaves the key free.
-- Receipts are kept forever.
CREATE TABLE IF NOT EXISTS rcon_grants (
    idempotency_key VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    user_id INT NOT NULL,
    payload_sha256 CHAR(64) CHARACTER SET ascii NOT NULL,
    status VARCHAR(16) CHARACTER SET ascii NOT NULL DEFAULT 'applied',
    result_json MEDIUMTEXT NOT NULL,
    created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (idempotency_key),
    KEY idx_rcon_grants_user (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
