-- Backfill before removing the legacy columns so upgrades preserve every setting.
SET @users_migration_time_zone = @@SESSION.time_zone;
SET SESSION time_zone = '+00:00';
CREATE TABLE `users_settings` (
  `user_id` INT(11) NOT NULL,
  `home_room` INT(10) UNSIGNED NOT NULL DEFAULT 0,
  `is_muted` BOOL NOT NULL DEFAULT FALSE,
  `block_newfriends` BOOL NOT NULL DEFAULT FALSE,
  `hide_online` BOOL NOT NULL DEFAULT FALSE,
  `hide_inroom` BOOL NOT NULL DEFAULT FALSE,
  `volume` VARCHAR(15) NOT NULL DEFAULT '100,100,100',
  `focus_preference` BOOL NOT NULL DEFAULT FALSE,
  `chat_preference` BOOL NOT NULL DEFAULT FALSE,
  `pets_muted` BOOL NOT NULL DEFAULT FALSE,
  `bots_muted` BOOL NOT NULL DEFAULT FALSE,
  `advertising_report_blocked` BOOL NOT NULL DEFAULT FALSE,
  `ignore_invites` BOOL NOT NULL DEFAULT FALSE,
  `allow_gifts` BOOL NOT NULL DEFAULT TRUE,
  `friend_bar_state` BOOL NOT NULL DEFAULT TRUE,
  `disable_forced_effects` BOOL NOT NULL DEFAULT FALSE,
  `allow_mimic` BOOL NOT NULL DEFAULT TRUE,
  PRIMARY KEY (`user_id`),
  CONSTRAINT `fk_users_settings_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

INSERT INTO `users_settings` (`user_id`, `home_room`, `is_muted`, `block_newfriends`, `hide_online`, `hide_inroom`, `volume`,
  `focus_preference`, `chat_preference`, `pets_muted`, `bots_muted`, `advertising_report_blocked`, `ignore_invites`,
  `allow_gifts`, `friend_bar_state`, `disable_forced_effects`, `allow_mimic`)
SELECT `id`, COALESCE(`home_room`, 0), COALESCE(`is_muted`, 0) = '1', COALESCE(`block_newfriends`, 0) = '1',
  COALESCE(`hide_online`, 0) = '1', COALESCE(`hide_inroom`, 0) = '1', COALESCE(`volume`, '100,100,100'),
  COALESCE(`focus_preference`, 0) = '1', COALESCE(`chat_preference`, 0) = '1', COALESCE(`pets_muted`, 0) = '1',
  COALESCE(`bots_muted`, 0) = '1', COALESCE(`advertising_report_blocked`, 0) = '1', COALESCE(`ignore_invites`, 0) = '1',
  COALESCE(`allow_gifts`, 1) = '1', COALESCE(`friend_bar_state`, 1) = '1', COALESCE(`disable_forced_effects`, 0) = '1',
  COALESCE(`allow_mimic`, 1) = '1'
FROM `users`
ON DUPLICATE KEY UPDATE
  `home_room` = VALUES(`home_room`), `is_muted` = VALUES(`is_muted`), `block_newfriends` = VALUES(`block_newfriends`),
  `hide_online` = VALUES(`hide_online`), `hide_inroom` = VALUES(`hide_inroom`), `volume` = VALUES(`volume`),
  `focus_preference` = VALUES(`focus_preference`), `chat_preference` = VALUES(`chat_preference`),
  `pets_muted` = VALUES(`pets_muted`), `bots_muted` = VALUES(`bots_muted`),
  `advertising_report_blocked` = VALUES(`advertising_report_blocked`), `ignore_invites` = VALUES(`ignore_invites`),
  `allow_gifts` = VALUES(`allow_gifts`), `friend_bar_state` = VALUES(`friend_bar_state`),
  `disable_forced_effects` = VALUES(`disable_forced_effects`), `allow_mimic` = VALUES(`allow_mimic`);

ALTER TABLE `users`
  MODIFY COLUMN `online` VARCHAR(1) NULL DEFAULT '0',
  MODIFY COLUMN `vip` VARCHAR(1) NULL DEFAULT '1';

ALTER TABLE `users`
  MODIFY COLUMN `online` BOOL NULL DEFAULT FALSE,
  MODIFY COLUMN `vip` BOOL NULL DEFAULT TRUE,
  ADD COLUMN `account_created_at` DATETIME NULL,
  ADD COLUMN `last_online_at` DATETIME NULL,
  ADD COLUMN `last_change_at` DATETIME NULL;

UPDATE `users` SET
  `account_created_at` = CASE WHEN CAST(`account_created` AS UNSIGNED) = 0 THEN NULL ELSE DATE_ADD('1970-01-01 00:00:00', INTERVAL CAST(`account_created` AS UNSIGNED) SECOND) END,
  `last_online_at` = CASE WHEN CAST(`last_online` AS UNSIGNED) = 0 THEN NULL ELSE DATE_ADD('1970-01-01 00:00:00', INTERVAL CAST(`last_online` AS UNSIGNED) SECOND) END,
  `last_change_at` = CASE WHEN CAST(`last_change` AS UNSIGNED) = 0 THEN NULL ELSE DATE_ADD('1970-01-01 00:00:00', INTERVAL CAST(`last_change` AS UNSIGNED) SECOND) END;

ALTER TABLE `users`
  DROP COLUMN `home_room`, DROP COLUMN `is_muted`, DROP COLUMN `block_newfriends`, DROP COLUMN `hide_online`,
  DROP COLUMN `hide_inroom`, DROP COLUMN `volume`, DROP COLUMN `focus_preference`, DROP COLUMN `chat_preference`,
  DROP COLUMN `pets_muted`, DROP COLUMN `bots_muted`, DROP COLUMN `advertising_report_blocked`, DROP COLUMN `ignore_invites`,
  DROP COLUMN `allow_gifts`, DROP COLUMN `friend_bar_state`, DROP COLUMN `disable_forced_effects`, DROP COLUMN `allow_mimic`,
  DROP COLUMN `account_created`, DROP COLUMN `last_online`, DROP COLUMN `last_change`,
  RENAME COLUMN `account_created_at` TO `account_created`, RENAME COLUMN `last_online_at` TO `last_online`,
  RENAME COLUMN `last_change_at` TO `last_change`, ADD INDEX `last_online` (`last_online`);

ALTER TABLE `user_info` ADD COLUMN `trading_locked_at` DATETIME NULL;
UPDATE `user_info` SET `trading_locked_at` = CASE WHEN CAST(`trading_locked` AS UNSIGNED) = 0 THEN NULL ELSE DATE_ADD('1970-01-01 00:00:00', INTERVAL CAST(`trading_locked` AS UNSIGNED) SECOND) END;
ALTER TABLE `user_info` DROP COLUMN `trading_locked`, RENAME COLUMN `trading_locked_at` TO `trading_locked`;

SET SESSION time_zone = @users_migration_time_zone;
