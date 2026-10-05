-- Trade audit timestamps were Unix seconds stored as text.
-- Empty, nonnumeric, nonpositive and unrepresentable values are unknown.
ALTER TABLE `logs_client_trade`
  ADD COLUMN `timestamp_utc` DATETIME(6) NULL AFTER `timestamp`;

UPDATE `logs_client_trade`
SET `timestamp_utc` = CASE
      WHEN `timestamp` IS NULL OR TRIM(`timestamp`) NOT REGEXP '^[+]?[0-9]+([.][0-9]+)?$' THEN NULL
      WHEN CAST(TRIM(`timestamp`) AS DECIMAL(40,20)) <= 0
        OR CAST(TRIM(`timestamp`) AS DECIMAL(40,20)) > 253402300799.999999 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)),
        INTERVAL CAST(ROUND(CAST(TRIM(`timestamp`) AS DECIMAL(40,20)) * 1000000) AS SIGNED) MICROSECOND)
    END;

ALTER TABLE `logs_client_trade`
  DROP COLUMN `timestamp`,
  CHANGE COLUMN `timestamp_utc` `timestamp` DATETIME(6) NULL DEFAULT NULL;
