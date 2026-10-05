-- Convert legacy Unix seconds without depending on the MariaDB session time zone.
-- Zero and NULL represented unknown expiry and remain NULL.
ALTER TABLE room_bans
    ADD COLUMN expires_at_utc DATETIME(6) NULL AFTER expire;

UPDATE room_bans
SET expires_at_utc = CASE
    WHEN expire IS NULL OR expire <= 0 THEN NULL
    WHEN expire >= 253402300800 THEN NULL
    WHEN CAST(expire AS DECIMAL(30, 6)) > 253402300799.999999 THEN NULL
    ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL expire SECOND)
END;

ALTER TABLE room_bans
    DROP COLUMN expire,
    CHANGE COLUMN expires_at_utc expire DATETIME(6) NULL;
