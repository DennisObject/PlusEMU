ALTER TABLE users_habbicons
    ADD COLUMN last_used_utc DATETIME(6) NULL DEFAULT NULL AFTER last_used;

UPDATE users_habbicons
SET last_used_utc = CASE
    WHEN last_used IS NULL OR last_used <= 0 OR last_used > 253402300799999 THEN NULL
    ELSE DATE_ADD(
        DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL FLOOR(last_used / 1000) SECOND),
        INTERVAL MOD(last_used, 1000) * 1000 MICROSECOND)
    END;

ALTER TABLE users_habbicons
    DROP INDEX recent,
    DROP COLUMN last_used,
    CHANGE COLUMN last_used_utc last_used DATETIME(6) NULL DEFAULT NULL,
    ADD KEY recent (user_id, last_used);
