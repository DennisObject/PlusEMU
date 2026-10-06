-- Legacy nonpositive/unknown promotion expiries mean no expiry.
-- Convert Unix seconds independently of the MariaDB session time zone.
ALTER TABLE `catalog_promotions`
  ADD COLUMN `expires_at_utc` DATETIME(6) NULL AFTER `expires_at`;

UPDATE `catalog_promotions`
SET `expires_at_utc` = CASE
      WHEN `expires_at` IS NULL OR `expires_at` <= 0 OR `expires_at` > 253402300799.999999 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`expires_at` * 1000000) AS SIGNED) MICROSECOND)
    END;

ALTER TABLE `catalog_promotions`
  DROP COLUMN `expires_at`,
  CHANGE COLUMN `expires_at_utc` `expires_at` DATETIME(6) NULL DEFAULT NULL;
