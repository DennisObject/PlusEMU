-- Convert legacy Unix seconds without depending on the MariaDB session time zone.
-- Zero, negative and NULL represented unknown log times and remain NULL.
-- Fractional seconds are retained to the microsecond and valid post-2038 values are preserved.
ALTER TABLE `logs_client_namechange`
    ADD COLUMN `timestamp_utc` DATETIME(6) NULL AFTER `timestamp`;

UPDATE `logs_client_namechange`
SET `timestamp_utc` = CASE
    WHEN `timestamp` IS NULL OR `timestamp` <= 0 THEN NULL
    WHEN `timestamp` >= 253402300800 THEN NULL
    WHEN CAST(`timestamp` AS DECIMAL(30, 6)) > 253402300799.999999 THEN NULL
    ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`timestamp` * 1000000) MICROSECOND)
END;

ALTER TABLE `logs_client_namechange`
    DROP COLUMN `timestamp`,
    CHANGE COLUMN `timestamp_utc` `timestamp` DATETIME(6) NULL;
