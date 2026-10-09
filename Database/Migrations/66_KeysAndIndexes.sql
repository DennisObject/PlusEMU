-- Primary and unique keys that say what a row is, indexes for the queries the emulator and CMS run on logins, room loads
-- and packets, and no index that nothing uses. Duplicate rows a new key forbids are removed first, keeping the oldest.
-- Apply while PlusEMU is stopped.

DELETE a FROM `room_rights` a JOIN `room_rights` b ON b.`room_id` = a.`room_id` AND b.`user_id` = a.`user_id` AND b.`id` < a.`id`;
DELETE a FROM `room_filter` a JOIN `room_filter` b ON b.`room_id` = a.`room_id` AND b.`word` = a.`word` AND b.`id` < a.`id`;
DELETE a FROM `messenger_requests` a JOIN `messenger_requests` b ON b.`from_id` = a.`from_id` AND b.`to_id` = a.`to_id` AND b.`id` < a.`id`;
DELETE a FROM `user_favorites` a JOIN `user_favorites` b ON b.`user_id` = a.`user_id` AND b.`room_id` = a.`room_id` AND b.`id` < a.`id`;
DELETE a FROM `user_presents` a JOIN `user_presents` b ON b.`item_id` = a.`item_id` AND b.`id` < a.`id`;
DELETE a FROM `user_vouchers` a JOIN `user_vouchers` b ON b.`user_id` = a.`user_id` AND b.`voucher` = a.`voucher` AND b.`id` < a.`id`;
DELETE a FROM `room_items_tele_links` a JOIN `room_items_tele_links` b ON b.`tele_one_id` = a.`tele_one_id` AND b.`id` < a.`id`;
DELETE a FROM `server_reward_logs` a JOIN `server_reward_logs` b ON b.`user_id` = a.`user_id` AND b.`reward_id` = a.`reward_id` AND b.`id` < a.`id`;
DELETE a FROM `group_memberships` a JOIN `group_memberships` b ON b.`group_id` = a.`group_id` AND b.`user_id` = a.`user_id` AND b.`id` < a.`id`;
DELETE a FROM `user_clothing` a JOIN `user_clothing` b ON b.`user_id` = a.`user_id` AND b.`part_id` = a.`part_id` AND b.`id` < a.`id`;
DELETE a FROM `catalog_marketplace_data` a JOIN `catalog_marketplace_data` b ON b.`sprite` = a.`sprite` AND b.`id` < a.`id`;
DELETE a FROM `talents` a JOIN `talents` b ON b.`type` = a.`type` AND b.`level` <=> a.`level` AND b.`id` < a.`id`;
-- group_requests and catalog_pet_races have no id to choose by; identical rows collapse to one.
CREATE TEMPORARY TABLE `migration_66_group_requests` AS SELECT DISTINCT * FROM `group_requests`;
DELETE FROM `group_requests`;
INSERT INTO `group_requests` SELECT * FROM `migration_66_group_requests`;
DROP TEMPORARY TABLE `migration_66_group_requests`;
CREATE TEMPORARY TABLE `migration_66_pet_races` AS
    SELECT `raceid`, `color1`, `color2`, MAX(`has1color`) AS `has1color`, MAX(`has2color`) AS `has2color`
    FROM `catalog_pet_races` WHERE `raceid` IS NOT NULL AND `color1` IS NOT NULL AND `color2` IS NOT NULL GROUP BY `raceid`, `color1`, `color2`;
DELETE FROM `catalog_pet_races`;
INSERT INTO `catalog_pet_races` (`raceid`, `color1`, `color2`, `has1color`, `has2color`) SELECT * FROM `migration_66_pet_races`;
DROP TEMPORARY TABLE `migration_66_pet_races`;

-- Natural keys instead of surrogate ids nothing reads; each also stops duplicate rows.
ALTER TABLE `room_rights` DROP PRIMARY KEY, DROP COLUMN `id`, DROP INDEX IF EXISTS `room_id`, ADD PRIMARY KEY (`room_id`, `user_id`);
ALTER TABLE `room_filter` DROP PRIMARY KEY, DROP COLUMN `id`, DROP INDEX IF EXISTS `room_id`, DROP INDEX IF EXISTS `word`,
    ADD PRIMARY KEY (`room_id`, `word`);
ALTER TABLE `messenger_requests` DROP PRIMARY KEY, DROP COLUMN `id`, DROP INDEX IF EXISTS `from_id`, ADD PRIMARY KEY (`from_id`, `to_id`);
ALTER TABLE `user_favorites` DROP PRIMARY KEY, DROP COLUMN `id`, DROP INDEX IF EXISTS `user_id`, ADD PRIMARY KEY (`user_id`, `room_id`);
ALTER TABLE `user_presents` DROP PRIMARY KEY, DROP COLUMN `id`, DROP INDEX IF EXISTS `item_id`, ADD PRIMARY KEY (`item_id`);
ALTER TABLE `user_vouchers` DROP PRIMARY KEY, DROP COLUMN `id`, DROP INDEX IF EXISTS `user_id, voucher`, ADD PRIMARY KEY (`user_id`, `voucher`);
ALTER TABLE `room_items_tele_links` DROP PRIMARY KEY, DROP COLUMN `id`, DROP INDEX IF EXISTS `tele_one_id`,
    ADD PRIMARY KEY (`tele_one_id`), ADD KEY `tele_two_id` (`tele_two_id`);
ALTER TABLE `server_reward_logs` DROP PRIMARY KEY, DROP COLUMN `id`, ADD PRIMARY KEY (`user_id`, `reward_id`);
ALTER TABLE `group_requests` DROP INDEX IF EXISTS `groupid`, ADD PRIMARY KEY (`group_id`, `user_id`);
ALTER TABLE `catalog_pet_races` MODIFY `raceid` INT NOT NULL, MODIFY `color1` INT NOT NULL, MODIFY `color2` INT NOT NULL,
    ADD PRIMARY KEY (`raceid`, `color1`, `color2`);

-- Unique keys the code already assumes.
ALTER TABLE `group_memberships` DROP INDEX IF EXISTS `groupid`, DROP INDEX IF EXISTS `rank`, ADD UNIQUE KEY `group_user` (`group_id`, `user_id`);
ALTER TABLE `user_clothing` MODIFY `part_id` INT UNSIGNED NOT NULL, DROP INDEX IF EXISTS `user_id`, ADD UNIQUE KEY `user_part` (`user_id`, `part_id`);
ALTER TABLE `catalog_marketplace_data` ADD UNIQUE KEY `sprite` (`sprite`);
ALTER TABLE `talents` ADD UNIQUE KEY `type_level` (`type`, `level`);

-- Group ranks were an ENUM('0','1','2') written with numbers, so MariaDB stored 1 (administrator) as '0' (member).
-- Members are 0 and administrators 1, and a group's owner is always an administrator.
ALTER TABLE `group_memberships` ADD COLUMN `rank_number` TINYINT UNSIGNED NOT NULL DEFAULT 0 AFTER `rank`;
UPDATE `group_memberships` SET `rank_number` = IF(CAST(`rank` AS CHAR) IN ('1', '2'), 1, 0);
UPDATE `group_memberships` m JOIN `groups` g ON g.`id` = m.`group_id` AND g.`owner_id` = m.`user_id` SET m.`rank_number` = 1;
ALTER TABLE `group_memberships` DROP COLUMN `rank`, CHANGE `rank_number` `rank` TINYINT UNSIGNED NOT NULL DEFAULT 0;

-- Room owners are user ids, stored as text, so neither the owner index nor a join to users could be used.
ALTER TABLE `rooms` MODIFY `owner` INT NOT NULL;

-- Indexes that duplicate the primary key or the start of another index.
ALTER TABLE `ambassador_logs` DROP INDEX IF EXISTS `id`;
ALTER TABLE `badge_definitions` DROP INDEX IF EXISTS `code`;
ALTER TABLE `bots` DROP INDEX IF EXISTS `id`;
ALTER TABLE `bots_petdata` DROP INDEX IF EXISTS `id`;
ALTER TABLE `chatlogs_console_invitations` DROP INDEX IF EXISTS `id`;
ALTER TABLE `furniture` DROP INDEX IF EXISTS `id`;
ALTER TABLE `games_config` DROP INDEX IF EXISTS `id`;
ALTER TABLE `groups` DROP INDEX IF EXISTS `id`;
ALTER TABLE `items_groups` DROP INDEX IF EXISTS `id`;
ALTER TABLE `items_youtube` DROP INDEX IF EXISTS `id`;
ALTER TABLE `messenger_friendships` DROP INDEX IF EXISTS `user_one_id`;
ALTER TABLE `room_bans` DROP INDEX IF EXISTS `user_id`;
ALTER TABLE `room_items_toner` DROP INDEX IF EXISTS `id`, DROP INDEX IF EXISTS `enabled`;
ALTER TABLE `room_items_moodlight` DROP INDEX IF EXISTS `enabled`;
ALTER TABLE `room_models` DROP INDEX IF EXISTS `id`;
ALTER TABLE `server_landing` DROP INDEX IF EXISTS `id`;
ALTER TABLE `user_achievements` DROP INDEX IF EXISTS `id`;
ALTER TABLE `user_badges` DROP INDEX IF EXISTS `user_id`;
ALTER TABLE `user_ignores` DROP INDEX IF EXISTS `user_id`;
ALTER TABLE `user_info` DROP INDEX IF EXISTS `user_id`;
ALTER TABLE `wired_items` DROP INDEX IF EXISTS `id`;
ALTER TABLE `wordfilter` DROP INDEX IF EXISTS `word`;
ALTER TABLE `user_saved_searches` DROP INDEX IF EXISTS `value`;

-- users: point lookups by id, username, mail, SSO ticket and IP; nothing filters on rank, credits or ip_reg.
ALTER TABLE `users` DROP INDEX IF EXISTS `id`, DROP INDEX IF EXISTS `rank`, DROP INDEX IF EXISTS `ip_reg`,
    DROP INDEX IF EXISTS `credits`, DROP INDEX IF EXISTS `messenger`;
ALTER TABLE `user_statistics` DROP INDEX IF EXISTS `id`, ADD KEY `AchievementScore` (`AchievementScore`);
-- The CMS ranks duckets and diamonds per currency type.
ALTER TABLE `user_currencies` ADD KEY `type_amount` (`type`, `amount`);

-- rooms: owner lookups for My Rooms, the navigator and every login; nothing filters on roomtype, score or category.
ALTER TABLE `rooms` DROP INDEX IF EXISTS `id`, DROP INDEX IF EXISTS `roomtype`, DROP INDEX IF EXISTS `score`,
    DROP INDEX IF EXISTS `category`, DROP INDEX IF EXISTS `owner`, ADD KEY `owner_caption` (`owner`, `caption`);

-- items: inventories load by owner where room_id is 0, rooms by room_id; the CMS counts holdings per furni.
ALTER TABLE `items` DROP INDEX IF EXISTS `id`, DROP INDEX IF EXISTS `userid`, DROP INDEX IF EXISTS `base_item`,
    ADD KEY `user_room` (`user_id`, `room_id`), ADD KEY `base_user` (`base_item`, `user_id`);

-- Every login reads and locks the user's offline messages.
ALTER TABLE `messenger_offline_messages` ADD KEY `to_id` (`to_id`);

-- Leaving a room closes the user's latest visit to it.
ALTER TABLE `user_roomvisits` DROP INDEX IF EXISTS `user_id`, DROP INDEX IF EXISTS `entry_timestamp`, DROP INDEX IF EXISTS `exit_timestamp`,
    ADD KEY `user_room_entry` (`user_id`, `room_id`, `entry_timestamp`), ADD KEY `room_id` (`room_id`);

-- Logins check bans by type and value; the startup sweep removes expired ones.
ALTER TABLE `bans` DROP INDEX IF EXISTS `value`, DROP INDEX IF EXISTS `bantype`, ADD KEY `type_value_expire` (`bantype`, `value`, `expire`);

-- Moderator chat logs read a room's or user's lines newest first.
ALTER TABLE `chatlogs` DROP INDEX IF EXISTS `user_id`, DROP INDEX IF EXISTS `room_id`,
    ADD KEY `room_time` (`room_id`, `timestamp`), ADD KEY `user_id` (`user_id`, `id`);

-- Marketplace: own offers by user and state, the search by state and listing time.
ALTER TABLE `catalog_marketplace_offers` ADD KEY `user_state` (`user_id`, `state`), ADD KEY `state_listed` (`state`, `listed_at`),
    ADD KEY `item_id` (`item_id`);

-- Groups: forum lists filter on forum_enabled.
ALTER TABLE `groups` ADD KEY `forum_enabled` (`forum_enabled`);

-- Camera quota cleanup deletes by day.
ALTER TABLE `camera_quota` ADD KEY `quota_date` (`quota_date`);

-- Wired variables: holder reads skip the target kind; the manager page sorts by value.
ALTER TABLE `wired_variable_values` ADD KEY `definition_holder` (`definition_id`, `holder_id`),
    ADD KEY `definition_kind_value` (`definition_id`, `target_kind`, `value`);
