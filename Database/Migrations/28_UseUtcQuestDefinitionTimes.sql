-- Convert legacy Unix seconds without depending on the MariaDB session time zone.
-- Zero, negative and NULL represented unknown definition times and remain NULL.
-- Fractional seconds are retained to the microsecond and valid post-2038 values are preserved.
ALTER TABLE `quests`
    ADD COLUMN `unlocks_at_utc` DATETIME(6) NULL AFTER `timestamp_unlock`,
    ADD COLUMN `locks_at_utc` DATETIME(6) NULL AFTER `timestamp_lock`;

UPDATE `quests`
SET `unlocks_at_utc` = CASE
        WHEN `timestamp_unlock` IS NULL OR `timestamp_unlock` <= 0 OR CAST(`timestamp_unlock` AS DECIMAL(30, 6)) > 253402300799.999999 THEN NULL
        ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`timestamp_unlock` * 1000000) MICROSECOND)
    END,
    `locks_at_utc` = CASE
        WHEN `timestamp_lock` IS NULL OR `timestamp_lock` <= 0 OR CAST(`timestamp_lock` AS DECIMAL(30, 6)) > 253402300799.999999 THEN NULL
        ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`timestamp_lock` * 1000000) MICROSECOND)
    END;

ALTER TABLE `quests`
    DROP COLUMN `timestamp_unlock`,
    DROP COLUMN `timestamp_lock`,
    CHANGE COLUMN `unlocks_at_utc` `timestamp_unlock` DATETIME(6) NULL,
    CHANGE COLUMN `locks_at_utc` `timestamp_lock` DATETIME(6) NULL;
