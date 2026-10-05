-- Convert legacy Unix seconds for staff command logs and housekeeping audit logs to UTC DATETIME(6).
-- Converting through the epoch literal keeps the result independent of the MariaDB session time zone.
-- Zero, negative and NULL values have no real time, so they become NULL. Fractional seconds are kept to the microsecond;
-- values past 2038 keep their meaning. The housekeeping timestamp_action index is recreated on the converted column.
ALTER TABLE `logs_client_staff`
  ADD COLUMN `timestamp_utc` DATETIME(6) NULL AFTER `timestamp`;

UPDATE `logs_client_staff`
SET `timestamp_utc` = CASE
      WHEN `timestamp` IS NULL OR `timestamp` <= 0 OR CAST(`timestamp` AS DECIMAL(30, 6)) > 253402300799.999999 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`timestamp` * 1000000) AS SIGNED) MICROSECOND)
    END;

ALTER TABLE `logs_client_staff`
  DROP COLUMN `timestamp`,
  CHANGE COLUMN `timestamp_utc` `timestamp` DATETIME(6) NULL DEFAULT NULL;

ALTER TABLE `housekeeping_log`
  DROP INDEX `timestamp_action`,
  ADD COLUMN `timestamp_utc` DATETIME(6) NULL AFTER `timestamp`;

UPDATE `housekeeping_log`
SET `timestamp_utc` = CASE
      WHEN `timestamp` IS NULL OR `timestamp` <= 0 OR CAST(`timestamp` AS DECIMAL(30, 6)) > 253402300799.999999 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`timestamp` * 1000000) AS SIGNED) MICROSECOND)
    END;

ALTER TABLE `housekeeping_log`
  DROP COLUMN `timestamp`,
  CHANGE COLUMN `timestamp_utc` `timestamp` DATETIME(6) NULL DEFAULT NULL,
  ADD KEY `timestamp_action` (`timestamp`, `action`);
