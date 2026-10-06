-- Convert legacy Unix seconds without depending on the MariaDB session time zone.
-- Zero and NULL represented unknown times and remain NULL.
ALTER TABLE room_promotions
    ADD COLUMN started_at_utc DATETIME(6) NULL AFTER timestamp_start,
    ADD COLUMN expires_at_utc DATETIME(6) NULL AFTER timestamp_expire;

UPDATE room_promotions
SET started_at_utc = CASE
        WHEN timestamp_start IS NULL OR timestamp_start <= 0 THEN NULL
        ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL timestamp_start SECOND)
    END,
    expires_at_utc = CASE
        WHEN timestamp_expire IS NULL OR timestamp_expire <= 0 THEN NULL
        ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL timestamp_expire SECOND)
    END;

ALTER TABLE room_promotions
    DROP COLUMN timestamp_start,
    DROP COLUMN timestamp_expire,
    CHANGE COLUMN started_at_utc timestamp_start DATETIME(6) NULL,
    CHANGE COLUMN expires_at_utc timestamp_expire DATETIME(6) NULL;
