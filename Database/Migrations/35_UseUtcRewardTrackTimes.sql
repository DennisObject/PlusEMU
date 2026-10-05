-- Convert legacy Unix seconds for reward track windows and prize claim times to UTC DATETIME(6).
-- Converting through the epoch literal keeps the result independent of the MariaDB session time zone.
-- Zero, negative and NULL values mean an unbounded window or an unknown claim time, so they become NULL.
-- Whole seconds are converted exactly, and values past 2038 keep their meaning.
ALTER TABLE `reward_tracks`
  ADD COLUMN `starts_at_utc` DATETIME(6) NULL AFTER `starts_at`,
  ADD COLUMN `ends_at_utc` DATETIME(6) NULL AFTER `ends_at`;

UPDATE `reward_tracks`
SET `starts_at_utc` = CASE
      WHEN `starts_at` IS NULL OR `starts_at` <= 0 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`starts_at` * 1000000) AS SIGNED) MICROSECOND)
    END,
    `ends_at_utc` = CASE
      WHEN `ends_at` IS NULL OR `ends_at` <= 0 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`ends_at` * 1000000) AS SIGNED) MICROSECOND)
    END;

ALTER TABLE `reward_tracks`
  DROP COLUMN `starts_at`,
  DROP COLUMN `ends_at`,
  CHANGE COLUMN `starts_at_utc` `starts_at` DATETIME(6) NULL DEFAULT NULL,
  CHANGE COLUMN `ends_at_utc` `ends_at` DATETIME(6) NULL DEFAULT NULL;

ALTER TABLE `users_reward_track_prizes`
  ADD COLUMN `claimed_at_utc` DATETIME(6) NULL AFTER `claimed_at`;

UPDATE `users_reward_track_prizes`
SET `claimed_at_utc` = CASE
      WHEN `claimed_at` IS NULL OR `claimed_at` <= 0 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`claimed_at` * 1000000) AS SIGNED) MICROSECOND)
    END;

ALTER TABLE `users_reward_track_prizes`
  DROP COLUMN `claimed_at`,
  CHANGE COLUMN `claimed_at_utc` `claimed_at` DATETIME(6) NULL DEFAULT NULL;
