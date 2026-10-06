-- Convert legacy Unix seconds for private console logs and offline messenger messages to UTC DATETIME(6).
-- Converting through the epoch literal keeps the result independent of the MariaDB session time zone.
-- Zero, negative and out-of-range values have no real time, so they become NULL as unknown times, like ambassador logs.
-- Fractional seconds are kept to the microsecond.
ALTER TABLE `chatlogs_console`
  DROP INDEX `timestamp`,
  ADD COLUMN `timestamp_datetime` DATETIME(6) NULL AFTER `timestamp`;

UPDATE `chatlogs_console`
SET `timestamp_datetime` = CASE
      WHEN `timestamp` IS NULL OR `timestamp` <= 0 OR `timestamp` > 253402300799 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`timestamp` * 1000000) AS SIGNED) MICROSECOND)
    END;

ALTER TABLE `chatlogs_console`
  DROP COLUMN `timestamp`,
  CHANGE COLUMN `timestamp_datetime` `timestamp` DATETIME(6) NULL DEFAULT NULL,
  ADD KEY `timestamp` (`timestamp`);

ALTER TABLE `messenger_offline_messages`
  ADD COLUMN `timestamp_datetime` DATETIME(6) NULL AFTER `timestamp`;

UPDATE `messenger_offline_messages`
SET `timestamp_datetime` = CASE
      WHEN `timestamp` IS NULL OR `timestamp` <= 0 OR `timestamp` > 253402300799 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`timestamp` * 1000000) AS SIGNED) MICROSECOND)
    END;

ALTER TABLE `messenger_offline_messages`
  DROP COLUMN `timestamp`,
  CHANGE COLUMN `timestamp_datetime` `timestamp` DATETIME(6) NULL DEFAULT NULL;
