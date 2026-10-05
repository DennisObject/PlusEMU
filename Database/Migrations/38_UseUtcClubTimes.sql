-- Pre-flight: a keyed interval start or payday that is zero, negative or past 9999-12-31 has no storable instant and
-- cannot become a primary-key value. Inserting a duplicate key into this temporary table fails the same way in every
-- sql_mode, so no ALTER runs and every source row is left untouched. Nothing is deleted or given an invented epoch.
CREATE TEMPORARY TABLE club_utc_preflight (ok TINYINT(1) NOT NULL PRIMARY KEY) ENGINE=InnoDB;
INSERT INTO club_utc_preflight (ok) VALUES (1);
INSERT INTO club_utc_preflight (ok) SELECT 1 FROM club_membership_intervals WHERE started_at <= 0 OR started_at > 253402300799 LIMIT 1;
INSERT INTO club_utc_preflight (ok) SELECT 1 FROM club_paydays WHERE payday <= 0 OR payday > 253402300799 LIMIT 1;
DROP TEMPORARY TABLE club_utc_preflight;

-- Membership instants: zero or negative means unknown, so those values become NULL.
ALTER TABLE `user_club_memberships`
  ADD COLUMN `expires_at_utc` DATETIME(6) NULL AFTER `expires_at`,
  ADD COLUMN `started_at_utc` DATETIME(6) NULL AFTER `started_at`,
  ADD COLUMN `first_started_at_utc` DATETIME(6) NULL AFTER `first_started_at`,
  ADD COLUMN `modified_at_utc` DATETIME(6) NULL AFTER `modified_at`;

UPDATE `user_club_memberships`
SET `expires_at_utc` = CASE WHEN `expires_at` IS NULL OR `expires_at` <= 0 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`expires_at` * 1000000) AS SIGNED) MICROSECOND) END,
    `started_at_utc` = CASE WHEN `started_at` IS NULL OR `started_at` <= 0 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`started_at` * 1000000) AS SIGNED) MICROSECOND) END,
    `first_started_at_utc` = CASE WHEN `first_started_at` IS NULL OR `first_started_at` <= 0 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`first_started_at` * 1000000) AS SIGNED) MICROSECOND) END,
    `modified_at_utc` = CASE WHEN `modified_at` IS NULL OR `modified_at` <= 0 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`modified_at` * 1000000) AS SIGNED) MICROSECOND) END;

ALTER TABLE `user_club_memberships`
  DROP COLUMN `expires_at`, DROP COLUMN `started_at`, DROP COLUMN `first_started_at`, DROP COLUMN `modified_at`,
  CHANGE COLUMN `expires_at_utc` `expires_at` DATETIME(6) NULL DEFAULT NULL,
  CHANGE COLUMN `started_at_utc` `started_at` DATETIME(6) NULL DEFAULT NULL,
  CHANGE COLUMN `first_started_at_utc` `first_started_at` DATETIME(6) NULL DEFAULT NULL,
  CHANGE COLUMN `modified_at_utc` `modified_at` DATETIME(6) NULL DEFAULT NULL;

-- Interval primary key is (user_id, started_at). Its time values are positive after pre-flight, so the key is rebuilt in place.
ALTER TABLE `club_membership_intervals`
  ADD COLUMN `started_at_utc` DATETIME(6) NULL AFTER `started_at`,
  ADD COLUMN `expires_at_utc` DATETIME(6) NULL AFTER `expires_at`;

UPDATE `club_membership_intervals`
SET `started_at_utc` = DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`started_at` * 1000000) AS SIGNED) MICROSECOND),
    `expires_at_utc` = CASE WHEN `expires_at` IS NULL OR `expires_at` <= 0 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`expires_at` * 1000000) AS SIGNED) MICROSECOND) END;

ALTER TABLE `club_membership_intervals`
  DROP PRIMARY KEY,
  DROP COLUMN `started_at`, DROP COLUMN `expires_at`,
  CHANGE COLUMN `started_at_utc` `started_at` DATETIME(6) NOT NULL,
  CHANGE COLUMN `expires_at_utc` `expires_at` DATETIME(6) NULL DEFAULT NULL,
  ADD PRIMARY KEY (`user_id`, `started_at`);

-- Gift and spending instants: unknown values become NULL. The spending index is rebuilt on the converted column.
ALTER TABLE `club_gift_claims`
  ADD COLUMN `claimed_at_utc` DATETIME(6) NULL AFTER `claimed_at`;

UPDATE `club_gift_claims`
SET `claimed_at_utc` = CASE WHEN `claimed_at` IS NULL OR `claimed_at` <= 0 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`claimed_at` * 1000000) AS SIGNED) MICROSECOND) END;

ALTER TABLE `club_gift_claims`
  DROP COLUMN `claimed_at`,
  CHANGE COLUMN `claimed_at_utc` `claimed_at` DATETIME(6) NULL DEFAULT NULL;

ALTER TABLE `club_credit_spending`
  DROP KEY `user_id`,
  ADD COLUMN `spent_at_utc` DATETIME(6) NULL AFTER `spent_at`;

UPDATE `club_credit_spending`
SET `spent_at_utc` = CASE WHEN `spent_at` IS NULL OR `spent_at` <= 0 THEN NULL
      ELSE DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`spent_at` * 1000000) AS SIGNED) MICROSECOND) END;

ALTER TABLE `club_credit_spending`
  DROP COLUMN `spent_at`,
  CHANGE COLUMN `spent_at_utc` `spent_at` DATETIME(6) NULL DEFAULT NULL,
  ADD KEY `user_id` (`user_id`, `spent_at`);

-- Payday primary key is (user_id, payday). Pre-flight guarantees every stored payday is positive.
ALTER TABLE `club_paydays`
  ADD COLUMN `payday_utc` DATETIME(6) NULL AFTER `payday`;

UPDATE `club_paydays`
SET `payday_utc` = DATE_ADD(CAST('1970-01-01 00:00:00.000000' AS DATETIME(6)), INTERVAL CAST(ROUND(`payday` * 1000000) AS SIGNED) MICROSECOND);

ALTER TABLE `club_paydays`
  DROP PRIMARY KEY,
  DROP COLUMN `payday`,
  CHANGE COLUMN `payday_utc` `payday` DATETIME(6) NOT NULL,
  ADD PRIMARY KEY (`user_id`, `payday`);
