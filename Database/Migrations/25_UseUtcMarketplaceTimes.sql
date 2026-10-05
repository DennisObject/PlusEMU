-- Convert legacy Unix seconds without depending on the MariaDB session time zone.
-- Zero, negative and NULL represented unknown times and remain NULL, so such offers are never live.
-- Fractional seconds are kept to the microsecond, rounded once, without depending on the session time zone.
ALTER TABLE `catalog_marketplace_offers`
    ADD COLUMN `listed_at_utc` DATETIME(6) NULL AFTER `timestamp`;

UPDATE `catalog_marketplace_offers`
SET `listed_at_utc` = CASE
        WHEN `timestamp` IS NULL OR `timestamp` <= 0 OR CAST(`timestamp` AS DECIMAL(30, 6)) > 253402300799.999999 THEN NULL
        ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`timestamp` * 1000000) MICROSECOND)
    END;

ALTER TABLE `catalog_marketplace_offers`
    DROP COLUMN `timestamp`,
    CHANGE COLUMN `listed_at_utc` `listed_at` DATETIME(6) NULL;
