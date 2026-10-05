-- Convert legacy numeric Unix seconds without depending on the MariaDB session time zone.
-- Nonpositive, empty, nonnumeric and NULL values represented unknown times and remain NULL.
-- Fractional seconds are retained to the microsecond and valid post-2038 values are preserved.
ALTER TABLE `bans`
    ADD COLUMN `expire_utc` DATETIME(6) NULL AFTER `expire`,
    ADD COLUMN `added_date_utc` DATETIME(6) NULL AFTER `added_date`;

UPDATE `bans`
SET `expire_utc` = CASE
        WHEN `expire` IS NULL OR `expire` <= 0 THEN NULL
        ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`expire` * 1000000) MICROSECOND)
    END,
    `added_date_utc` = CASE
        WHEN `added_date` IS NULL
            OR TRIM(`added_date`) NOT REGEXP '^[0-9]+([.][0-9]+)?$'
            OR CAST(`added_date` AS DECIMAL(30, 6)) <= 0 THEN NULL
        ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00',
            INTERVAL ROUND(CAST(`added_date` AS DECIMAL(30, 6)) * 1000000) MICROSECOND)
    END;

ALTER TABLE `bans`
    DROP COLUMN `expire`,
    DROP COLUMN `added_date`,
    CHANGE COLUMN `expire_utc` `expire` DATETIME(6) NULL,
    CHANGE COLUMN `added_date_utc` `added_date` DATETIME(6) NULL;
