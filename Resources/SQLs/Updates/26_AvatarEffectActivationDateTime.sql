SET @effects_migration_time_zone = @@SESSION.time_zone;
SET SESSION time_zone = '+00:00';

ALTER TABLE `user_effects` ADD COLUMN `activated_at` DATETIME NULL;
UPDATE `user_effects`
SET `activated_at` = CASE
  WHEN CAST(`activated_stamp` AS UNSIGNED) = 0 THEN NULL
  ELSE DATE_ADD('1970-01-01 00:00:00', INTERVAL CAST(`activated_stamp` AS UNSIGNED) SECOND)
END;
ALTER TABLE `user_effects` DROP COLUMN `activated_stamp`, RENAME COLUMN `activated_at` TO `activated_stamp`;

SET SESSION time_zone = @effects_migration_time_zone;
