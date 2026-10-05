ALTER TABLE wired_variable_values
    ADD COLUMN created_at_utc DATETIME(6) NULL DEFAULT NULL AFTER value,
    ADD COLUMN updated_at_utc DATETIME(6) NULL DEFAULT NULL AFTER created_at_ms;

UPDATE wired_variable_values
SET created_at_utc = CASE
        WHEN created_at_ms <= 0 THEN NULL
        ELSE DATE_ADD(
            DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL FLOOR(created_at_ms / 1000) SECOND),
            INTERVAL MOD(created_at_ms, 1000) * 1000 MICROSECOND)
    END,
    updated_at_utc = CASE
        WHEN updated_at_ms <= 0 THEN NULL
        ELSE DATE_ADD(
            DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL FLOOR(updated_at_ms / 1000) SECOND),
            INTERVAL MOD(updated_at_ms, 1000) * 1000 MICROSECOND)
    END;

ALTER TABLE wired_variable_values
    DROP COLUMN created_at_ms,
    DROP COLUMN updated_at_ms,
    CHANGE COLUMN created_at_utc created_at DATETIME(6) NULL DEFAULT NULL,
    CHANGE COLUMN updated_at_utc updated_at DATETIME(6) NULL DEFAULT NULL;
