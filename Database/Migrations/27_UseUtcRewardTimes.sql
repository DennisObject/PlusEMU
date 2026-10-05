ALTER TABLE `server_rewards`
  ADD COLUMN `reward_start_datetime` DATETIME(6) NULL AFTER `reward_start`,
  ADD COLUMN `reward_end_datetime` DATETIME(6) NULL AFTER `reward_end`;

UPDATE `server_rewards`
SET `reward_start_datetime` = CASE
      WHEN `reward_start` IS NULL OR `reward_start` <= 0 THEN NULL
      WHEN `reward_start` >= 253402300800 THEN NULL
      WHEN CAST(`reward_start` AS DECIMAL(30, 6)) > 253402300799.999999 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL ROUND(`reward_start` * 1000000) MICROSECOND)
    END,
    `reward_end_datetime` = CASE
      WHEN `reward_end` IS NULL OR `reward_end` <= 0 THEN NULL
      WHEN `reward_end` >= 253402300800 THEN NULL
      WHEN CAST(`reward_end` AS DECIMAL(30, 6)) > 253402300799.999999 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL ROUND(`reward_end` * 1000000) MICROSECOND)
    END;

ALTER TABLE `server_rewards`
  DROP COLUMN `reward_start`,
  DROP COLUMN `reward_end`,
  CHANGE COLUMN `reward_start_datetime` `reward_start` DATETIME(6) NULL,
  CHANGE COLUMN `reward_end_datetime` `reward_end` DATETIME(6) NULL;
