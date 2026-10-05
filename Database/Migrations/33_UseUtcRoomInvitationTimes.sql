ALTER TABLE `chatlogs_console_invitations`
  ADD COLUMN `timestamp_datetime` DATETIME(6) NULL AFTER `timestamp`;

UPDATE `chatlogs_console_invitations`
SET `timestamp_datetime` = CASE
      WHEN `timestamp` IS NULL OR `timestamp` <= 0 OR CAST(`timestamp` AS DECIMAL(30, 6)) > 253402300799.999999 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL ROUND(`timestamp` * 1000000) MICROSECOND)
    END;

ALTER TABLE `chatlogs_console_invitations`
  DROP COLUMN `timestamp`,
  CHANGE COLUMN `timestamp_datetime` `timestamp` DATETIME(6) NULL;
