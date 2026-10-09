-- Foreign keys for the relations the emulator and CMS use, so a row can no longer point at something that is gone.
-- Child columns take their parent's exact type first; rows that already point nowhere are removed (or, for optional
-- references, cleared). Rows only meaningful for their parent go with it (CASCADE); definitions in use and rooms or
-- groups that still have an owner cannot be deleted (RESTRICT); audit and history tables keep no key on purpose.
-- Apply while PlusEMU is stopped. A statement whose table or column an install does not have is skipped.

-- 1. Child columns take their parent's type (user and room ids are signed, item and furniture ids unsigned).
SET @migration_67_checks = @@FOREIGN_KEY_CHECKS;
SET FOREIGN_KEY_CHECKS = 0;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('bots', 'room_id'))) = 1,
    'ALTER TABLE `bots` MODIFY `room_id` int(10) NOT NULL DEFAULT 0', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('bots', 'user_id'))) = 1,
    'ALTER TABLE `bots` MODIFY `user_id` int(11) NOT NULL DEFAULT 0', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('catalog_marketplace_offers', 'user_id'))) = 1,
    'ALTER TABLE `catalog_marketplace_offers` MODIFY `user_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_memberships', 'user_id'))) = 1,
    'ALTER TABLE `group_memberships` MODIFY `user_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_requests', 'user_id'))) = 1,
    'ALTER TABLE `group_requests` MODIFY `user_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('groups', 'owner_id'))) = 1,
    'ALTER TABLE `groups` MODIFY `owner_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('items', 'room_id'))) = 1,
    'ALTER TABLE `items` MODIFY `room_id` int(10) NOT NULL DEFAULT 0', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('items_groups', 'group_id'))) = 1,
    'ALTER TABLE `items_groups` MODIFY `group_id` int(11) unsigned NOT NULL DEFAULT 0', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_friendships', 'user_one_id'))) = 1,
    'ALTER TABLE `messenger_friendships` MODIFY `user_one_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_friendships', 'user_two_id'))) = 1,
    'ALTER TABLE `messenger_friendships` MODIFY `user_two_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_offline_messages', 'from_id'))) = 1,
    'ALTER TABLE `messenger_offline_messages` MODIFY `from_id` int(11) NOT NULL DEFAULT 0', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_offline_messages', 'to_id'))) = 1,
    'ALTER TABLE `messenger_offline_messages` MODIFY `to_id` int(11) NOT NULL DEFAULT 0', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_requests', 'from_id'))) = 1,
    'ALTER TABLE `messenger_requests` MODIFY `from_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_requests', 'to_id'))) = 1,
    'ALTER TABLE `messenger_requests` MODIFY `to_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('moderation_topic_actions', 'parent_id'))) = 1,
    'ALTER TABLE `moderation_topic_actions` MODIFY `parent_id` int(11) unsigned NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_bans', 'room_id'))) = 1,
    'ALTER TABLE `room_bans` MODIFY `room_id` int(10) NOT NULL DEFAULT 0', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_bans', 'user_id'))) = 1,
    'ALTER TABLE `room_bans` MODIFY `user_id` int(11) NOT NULL DEFAULT 0', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_polls', 'room_id'))) = 1,
    'ALTER TABLE `room_polls` MODIFY `room_id` int(10) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_rights', 'room_id'))) = 1,
    'ALTER TABLE `room_rights` MODIFY `room_id` int(10) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_rights', 'user_id'))) = 1,
    'ALTER TABLE `room_rights` MODIFY `user_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_wired_settings', 'room_id'))) = 1,
    'ALTER TABLE `room_wired_settings` MODIFY `room_id` int(10) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_achievements', 'userid'))) = 1,
    'ALTER TABLE `user_achievements` MODIFY `userid` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_badges', 'user_id'))) = 1,
    'ALTER TABLE `user_badges` MODIFY `user_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_effects', 'user_id'))) = 1,
    'ALTER TABLE `user_effects` MODIFY `user_id` int(11) NULL DEFAULT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_favorites', 'room_id'))) = 1,
    'ALTER TABLE `user_favorites` MODIFY `room_id` int(10) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_favorites', 'user_id'))) = 1,
    'ALTER TABLE `user_favorites` MODIFY `user_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_ignores', 'ignore_id'))) = 1,
    'ALTER TABLE `user_ignores` MODIFY `ignore_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_ignores', 'user_id'))) = 1,
    'ALTER TABLE `user_ignores` MODIFY `user_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_quests', 'user_id'))) = 1,
    'ALTER TABLE `user_quests` MODIFY `user_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_roomvisits', 'room_id'))) = 1,
    'ALTER TABLE `user_roomvisits` MODIFY `room_id` int(10) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_roomvisits', 'user_id'))) = 1,
    'ALTER TABLE `user_roomvisits` MODIFY `user_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_statistics', 'groupid'))) = 1,
    'ALTER TABLE `user_statistics` MODIFY `groupid` int(11) unsigned NOT NULL DEFAULT 0', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_wardrobe', 'user_id'))) = 1,
    'ALTER TABLE `user_wardrobe` MODIFY `user_id` int(11) NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('users_settings', 'home_room'))) = 1,
    'ALTER TABLE `users_settings` MODIFY `home_room` int(10) NOT NULL DEFAULT 0', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('wired_items', 'id'))) = 1,
    'ALTER TABLE `wired_items` MODIFY `id` int(10) unsigned NOT NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET FOREIGN_KEY_CHECKS = @migration_67_checks;

-- 2. Rows pointing at a parent that no longer exists.
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('bots', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `bots` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('bots_petdata', 'id'), ('bots', 'id'))) = 2,
    'DELETE c FROM `bots_petdata` c WHERE c.`id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `bots` p WHERE p.`id` = c.`id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('bots_speech', 'bot_id'), ('bots', 'id'))) = 2,
    'DELETE c FROM `bots_speech` c WHERE c.`bot_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `bots` p WHERE p.`id` = c.`bot_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_accounts', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `camera_accounts` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_competition_entries', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `camera_competition_entries` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_media', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `camera_media` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_publications', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `camera_publications` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_purchases', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `camera_purchases` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_purchases', 'item_id'), ('items', 'id'))) = 2,
    'DELETE c FROM `camera_purchases` c WHERE c.`item_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `items` p WHERE p.`id` = c.`item_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_quota', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `camera_quota` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('catalog_marketplace_offers', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `catalog_marketplace_offers` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('catalog_marketplace_offers', 'item_id'), ('furniture', 'id'))) = 2,
    'DELETE c FROM `catalog_marketplace_offers` c WHERE c.`item_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `furniture` p WHERE p.`id` = c.`item_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('club_credit_spending', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `club_credit_spending` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('club_gift_claims', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `club_gift_claims` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('club_membership_intervals', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `club_membership_intervals` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('club_paydays', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `club_paydays` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('crafting_altars_recipes', 'altar_item_id'), ('furniture', 'id'))) = 2,
    'DELETE c FROM `crafting_altars_recipes` c WHERE c.`altar_item_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `furniture` p WHERE p.`id` = c.`altar_item_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('crafting_recipes', 'reward_item_id'), ('furniture', 'id'))) = 2,
    'DELETE c FROM `crafting_recipes` c WHERE c.`reward_item_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `furniture` p WHERE p.`id` = c.`reward_item_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('crafting_recipes_ingredients', 'item_id'), ('furniture', 'id'))) = 2,
    'DELETE c FROM `crafting_recipes_ingredients` c WHERE c.`item_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `furniture` p WHERE p.`id` = c.`item_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_forum_messages', 'author_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `group_forum_messages` c WHERE c.`author_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`author_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_forum_post_limits', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `group_forum_post_limits` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_forum_read_markers', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `group_forum_read_markers` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_forum_threads', 'author_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `group_forum_threads` c WHERE c.`author_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`author_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_memberships', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `group_memberships` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_memberships', 'group_id'), ('groups', 'id'))) = 2,
    'DELETE c FROM `group_memberships` c WHERE c.`group_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `groups` p WHERE p.`id` = c.`group_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_requests', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `group_requests` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_requests', 'group_id'), ('groups', 'id'))) = 2,
    'DELETE c FROM `group_requests` c WHERE c.`group_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `groups` p WHERE p.`id` = c.`group_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('groups', 'owner_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `groups` c WHERE c.`owner_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`owner_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('items', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `items` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('items', 'base_item'), ('furniture', 'id'))) = 2,
    'DELETE c FROM `items` c WHERE c.`base_item` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `furniture` p WHERE p.`id` = c.`base_item`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('items_groups', 'id'), ('items', 'id'))) = 2,
    'DELETE c FROM `items_groups` c WHERE c.`id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `items` p WHERE p.`id` = c.`id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('items_groups', 'group_id'), ('groups', 'id'))) = 2,
    'DELETE c FROM `items_groups` c WHERE c.`group_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `groups` p WHERE p.`id` = c.`group_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_friendships', 'user_one_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `messenger_friendships` c WHERE c.`user_one_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_one_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_friendships', 'user_two_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `messenger_friendships` c WHERE c.`user_two_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_two_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_offline_messages', 'to_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `messenger_offline_messages` c WHERE c.`to_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`to_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_offline_messages', 'from_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `messenger_offline_messages` c WHERE c.`from_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`from_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_requests', 'from_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `messenger_requests` c WHERE c.`from_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`from_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_requests', 'to_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `messenger_requests` c WHERE c.`to_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`to_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('moderation_preset_action_messages', 'parent_id'), ('moderation_preset_action_categories', 'id'))) = 2,
    'DELETE c FROM `moderation_preset_action_messages` c WHERE c.`parent_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `moderation_preset_action_categories` p WHERE p.`id` = c.`parent_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('moderation_topic_actions', 'parent_id'), ('moderation_topics', 'id'))) = 2,
    'DELETE c FROM `moderation_topic_actions` c WHERE c.`parent_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `moderation_topics` p WHERE p.`id` = c.`parent_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('navigator_publics', 'room_id'), ('rooms', 'id'))) = 2,
    'DELETE c FROM `navigator_publics` c WHERE c.`room_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `rooms` p WHERE p.`id` = c.`room_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('rcon_grants', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `rcon_grants` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('recycler_prizes', 'item_id'), ('furniture', 'id'))) = 2,
    'DELETE c FROM `recycler_prizes` c WHERE c.`item_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `furniture` p WHERE p.`id` = c.`item_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('recycler_prizes', 'level'), ('recycler_levels', 'level'))) = 2,
    'DELETE c FROM `recycler_prizes` c WHERE c.`level` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `recycler_levels` p WHERE p.`level` = c.`level`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('reward_track_prizes', 'track_id'), ('reward_tracks', 'id'))) = 2,
    'DELETE c FROM `reward_track_prizes` c WHERE c.`track_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `reward_tracks` p WHERE p.`id` = c.`track_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('reward_track_tasks', 'track_id'), ('reward_tracks', 'id'))) = 2,
    'DELETE c FROM `reward_track_tasks` c WHERE c.`track_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `reward_tracks` p WHERE p.`id` = c.`track_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_bans', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `room_bans` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_bans', 'room_id'), ('rooms', 'id'))) = 2,
    'DELETE c FROM `room_bans` c WHERE c.`room_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `rooms` p WHERE p.`id` = c.`room_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_filter', 'room_id'), ('rooms', 'id'))) = 2,
    'DELETE c FROM `room_filter` c WHERE c.`room_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `rooms` p WHERE p.`id` = c.`room_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_items_moodlight', 'item_id'), ('items', 'id'))) = 2,
    'DELETE c FROM `room_items_moodlight` c WHERE c.`item_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `items` p WHERE p.`id` = c.`item_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_items_tele_links', 'tele_one_id'), ('items', 'id'))) = 2,
    'DELETE c FROM `room_items_tele_links` c WHERE c.`tele_one_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `items` p WHERE p.`id` = c.`tele_one_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_items_tele_links', 'tele_two_id'), ('items', 'id'))) = 2,
    'DELETE c FROM `room_items_tele_links` c WHERE c.`tele_two_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `items` p WHERE p.`id` = c.`tele_two_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_items_toner', 'id'), ('items', 'id'))) = 2,
    'DELETE c FROM `room_items_toner` c WHERE c.`id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `items` p WHERE p.`id` = c.`id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_music_disc_definitions', 'base_item'), ('furniture', 'id'))) = 2,
    'DELETE c FROM `room_music_disc_definitions` c WHERE c.`base_item` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `furniture` p WHERE p.`id` = c.`base_item`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_poll_questions', 'poll_id'), ('room_polls', 'id'))) = 2,
    'DELETE c FROM `room_poll_questions` c WHERE c.`poll_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `room_polls` p WHERE p.`id` = c.`poll_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_poll_responses', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `room_poll_responses` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_poll_responses', 'poll_id'), ('room_polls', 'id'))) = 2,
    'DELETE c FROM `room_poll_responses` c WHERE c.`poll_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `room_polls` p WHERE p.`id` = c.`poll_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_polls', 'room_id'), ('rooms', 'id'))) = 2,
    'DELETE c FROM `room_polls` c WHERE c.`room_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `rooms` p WHERE p.`id` = c.`room_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_promotions', 'room_id'), ('rooms', 'id'))) = 2,
    'DELETE c FROM `room_promotions` c WHERE c.`room_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `rooms` p WHERE p.`id` = c.`room_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_rights', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `room_rights` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_rights', 'room_id'), ('rooms', 'id'))) = 2,
    'DELETE c FROM `room_rights` c WHERE c.`room_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `rooms` p WHERE p.`id` = c.`room_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_wired_settings', 'room_id'), ('rooms', 'id'))) = 2,
    'DELETE c FROM `room_wired_settings` c WHERE c.`room_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `rooms` p WHERE p.`id` = c.`room_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('rooms', 'owner'), ('users', 'id'))) = 2,
    'DELETE c FROM `rooms` c WHERE c.`owner` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`owner`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('rooms', 'model_name'), ('room_models', 'id'))) = 2,
    'DELETE c FROM `rooms` c WHERE c.`model_name` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `room_models` p WHERE p.`id` = c.`model_name`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('rooms', 'category'), ('navigator_categories', 'id'))) = 2,
    'DELETE c FROM `rooms` c WHERE c.`category` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `navigator_categories` p WHERE p.`id` = c.`category`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('safety_quiz_questions', 'quiz_code'), ('safety_quizzes', 'code'))) = 2,
    'DELETE c FROM `safety_quiz_questions` c WHERE c.`quiz_code` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `safety_quizzes` p WHERE p.`code` = c.`quiz_code`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('server_reward_logs', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `server_reward_logs` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('server_reward_logs', 'reward_id'), ('server_rewards', 'id'))) = 2,
    'DELETE c FROM `server_reward_logs` c WHERE c.`reward_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `server_rewards` p WHERE p.`id` = c.`reward_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('snowwar_game_tokens', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `snowwar_game_tokens` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('snowwar_scores', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `snowwar_scores` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_access_tokens', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_access_tokens` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_access_tokens', 'session_id'), ('user_sessions', 'id'))) = 2,
    'DELETE c FROM `user_access_tokens` c WHERE c.`session_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `user_sessions` p WHERE p.`id` = c.`session_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_achievements', 'userid'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_achievements` c WHERE c.`userid` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`userid`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_badges', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_badges` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_calendar_claims', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_calendar_claims` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_calendar_claims', 'campaign_id'), ('campaign_calendars', 'id'))) = 2,
    'DELETE c FROM `user_calendar_claims` c WHERE c.`campaign_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `campaign_calendars` p WHERE p.`id` = c.`campaign_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_calendar_claims', 'reward_id'), ('campaign_calendar_rewards', 'id'))) = 2,
    'DELETE c FROM `user_calendar_claims` c WHERE c.`reward_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `campaign_calendar_rewards` p WHERE p.`id` = c.`reward_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_clothing', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_clothing` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_club_memberships', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_club_memberships` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_crafting_recipes', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_crafting_recipes` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_effects', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_effects` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_favorites', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_favorites` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_favorites', 'room_id'), ('rooms', 'id'))) = 2,
    'DELETE c FROM `user_favorites` c WHERE c.`room_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `rooms` p WHERE p.`id` = c.`room_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_ignores', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_ignores` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_ignores', 'ignore_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_ignores` c WHERE c.`ignore_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`ignore_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_info', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_info` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_permissions', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_permissions` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_permissions', 'granted_by'), ('users', 'id'))) = 2,
    'UPDATE `user_permissions` c SET c.`granted_by` = NULL WHERE c.`granted_by` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`granted_by`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_presents', 'item_id'), ('items', 'id'))) = 2,
    'DELETE c FROM `user_presents` c WHERE c.`item_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `items` p WHERE p.`id` = c.`item_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_presents', 'base_id'), ('furniture', 'id'))) = 2,
    'DELETE c FROM `user_presents` c WHERE c.`base_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `furniture` p WHERE p.`id` = c.`base_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_quests', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_quests` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_quests', 'quest_id'), ('quests', 'id'))) = 2,
    'DELETE c FROM `user_quests` c WHERE c.`quest_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `quests` p WHERE p.`id` = c.`quest_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_recycler', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_recycler` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_remember_tokens', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_remember_tokens` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_remember_tokens', 'family_id'), ('user_sessions', 'id'))) = 2,
    'DELETE c FROM `user_remember_tokens` c WHERE c.`family_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `user_sessions` p WHERE p.`id` = c.`family_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_roles', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_roles` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_roles', 'granted_by'), ('users', 'id'))) = 2,
    'UPDATE `user_roles` c SET c.`granted_by` = NULL WHERE c.`granted_by` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`granted_by`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_roomvisits', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_roomvisits` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_roomvisits', 'room_id'), ('rooms', 'id'))) = 2,
    'DELETE c FROM `user_roomvisits` c WHERE c.`room_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `rooms` p WHERE p.`id` = c.`room_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_safety_quizzes', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_safety_quizzes` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_safety_quizzes', 'quiz_code'), ('safety_quizzes', 'code'))) = 2,
    'DELETE c FROM `user_safety_quizzes` c WHERE c.`quiz_code` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `safety_quizzes` p WHERE p.`code` = c.`quiz_code`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_saved_searches', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_saved_searches` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_sessions', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_sessions` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_statistics', 'id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_statistics` c WHERE c.`id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_talent_rewards', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_talent_rewards` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_vouchers', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_vouchers` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_vouchers', 'voucher'), ('catalog_vouchers', 'voucher'))) = 2,
    'DELETE c FROM `user_vouchers` c WHERE c.`voucher` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `catalog_vouchers` p WHERE p.`voucher` = c.`voucher`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_wardrobe', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `user_wardrobe` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('users_habbicons', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `users_habbicons` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('users_reward_track_prizes', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `users_reward_track_prizes` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('users_reward_track_tasks', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `users_reward_track_tasks` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('users_reward_tracks', 'user_id'), ('users', 'id'))) = 2,
    'DELETE c FROM `users_reward_tracks` c WHERE c.`user_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `users` p WHERE p.`id` = c.`user_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('users_reward_tracks', 'track_id'), ('reward_tracks', 'id'))) = 2,
    'DELETE c FROM `users_reward_tracks` c WHERE c.`track_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `reward_tracks` p WHERE p.`id` = c.`track_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('wired_item_configurations', 'item_id'), ('items', 'id'))) = 2,
    'DELETE c FROM `wired_item_configurations` c WHERE c.`item_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `items` p WHERE p.`id` = c.`item_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('wired_items', 'id'), ('items', 'id'))) = 2,
    'DELETE c FROM `wired_items` c WHERE c.`id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `items` p WHERE p.`id` = c.`id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('wired_reward_state', 'item_id'), ('items', 'id'))) = 2,
    'DELETE c FROM `wired_reward_state` c WHERE c.`item_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `items` p WHERE p.`id` = c.`item_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('wired_variable_values', 'definition_id'), ('items', 'id'))) = 2,
    'DELETE c FROM `wired_variable_values` c WHERE c.`definition_id` IS NOT NULL AND NOT EXISTS (SELECT 1 FROM `items` p WHERE p.`id` = c.`definition_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('items', 'room_id'), ('rooms', 'id'))) = 2,
    'UPDATE `items` c SET c.`room_id` = 0 WHERE c.`room_id` <> 0 AND NOT EXISTS (SELECT 1 FROM `rooms` p WHERE p.`id` = c.`room_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('bots', 'room_id'), ('rooms', 'id'))) = 2,
    'UPDATE `bots` c SET c.`room_id` = 0 WHERE c.`room_id` <> 0 AND NOT EXISTS (SELECT 1 FROM `rooms` p WHERE p.`id` = c.`room_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('users_settings', 'home_room'), ('rooms', 'id'))) = 2,
    'UPDATE `users_settings` c SET c.`home_room` = 0 WHERE c.`home_room` <> 0 AND NOT EXISTS (SELECT 1 FROM `rooms` p WHERE p.`id` = c.`home_room`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('rooms', 'group_id'), ('groups', 'id'))) = 2,
    'UPDATE `rooms` c SET c.`group_id` = 0 WHERE c.`group_id` <> 0 AND NOT EXISTS (SELECT 1 FROM `groups` p WHERE p.`id` = c.`group_id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_statistics', 'groupid'), ('groups', 'id'))) = 2,
    'UPDATE `user_statistics` c SET c.`groupid` = 0 WHERE c.`groupid` <> 0 AND NOT EXISTS (SELECT 1 FROM `groups` p WHERE p.`id` = c.`groupid`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;

-- 3. Jukebox playlists and crafting recipes follow their player, disc or recipe instead of blocking its removal.
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_music_playlist', 'player_id'), ('room_music_players', 'item_id'))) = 2,
    'ALTER TABLE `room_music_playlist` DROP FOREIGN KEY IF EXISTS `room_music_playlist_ibfk_1`, ADD CONSTRAINT `fk_room_music_playlist_player_id` FOREIGN KEY (`player_id`) REFERENCES `room_music_players` (`item_id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_music_playlist', 'disc_id'), ('items', 'id'))) = 2,
    'ALTER TABLE `room_music_playlist` DROP FOREIGN KEY IF EXISTS `room_music_playlist_ibfk_2`, ADD CONSTRAINT `fk_room_music_playlist_disc_id` FOREIGN KEY (`disc_id`) REFERENCES `items` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('crafting_altars_recipes', 'recipe_id'), ('crafting_recipes', 'id'))) = 2,
    'ALTER TABLE `crafting_altars_recipes` DROP FOREIGN KEY IF EXISTS `crafting_altars_recipes_ibfk_1`, ADD CONSTRAINT `fk_crafting_altars_recipes_recipe_id` FOREIGN KEY (`recipe_id`) REFERENCES `crafting_recipes` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('crafting_recipes_ingredients', 'recipe_id'), ('crafting_recipes', 'id'))) = 2,
    'ALTER TABLE `crafting_recipes_ingredients` DROP FOREIGN KEY IF EXISTS `crafting_recipes_ingredients_ibfk_1`, ADD CONSTRAINT `fk_crafting_recipes_ingredients_recipe_id` FOREIGN KEY (`recipe_id`) REFERENCES `crafting_recipes` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_crafting_recipes', 'recipe_id'), ('crafting_recipes', 'id'))) = 2,
    'ALTER TABLE `user_crafting_recipes` DROP FOREIGN KEY IF EXISTS `user_crafting_recipes_ibfk_1`, ADD CONSTRAINT `fk_user_crafting_recipes_recipe_id` FOREIGN KEY (`recipe_id`) REFERENCES `crafting_recipes` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;

-- 4. Where the code writes 0 for "none" (inventory, no home room, no group), a stored NULLIF(column, 0) carries the key.
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('items', 'room_id'), ('rooms', 'id'))) = 2,
    'ALTER TABLE `items` ADD COLUMN `room_ref` int(10) GENERATED ALWAYS AS (NULLIF(`room_id`, 0)) STORED, ADD CONSTRAINT `fk_items_room_ref` FOREIGN KEY (`room_ref`) REFERENCES `rooms` (`id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('bots', 'room_id'), ('rooms', 'id'))) = 2,
    'ALTER TABLE `bots` ADD COLUMN `room_ref` int(10) GENERATED ALWAYS AS (NULLIF(`room_id`, 0)) STORED, ADD CONSTRAINT `fk_bots_room_ref` FOREIGN KEY (`room_ref`) REFERENCES `rooms` (`id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('users_settings', 'home_room'), ('rooms', 'id'))) = 2,
    'ALTER TABLE `users_settings` ADD COLUMN `home_room_ref` int(10) GENERATED ALWAYS AS (NULLIF(`home_room`, 0)) STORED, ADD CONSTRAINT `fk_users_settings_home_room_ref` FOREIGN KEY (`home_room_ref`) REFERENCES `rooms` (`id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('rooms', 'group_id'), ('groups', 'id'))) = 2,
    'ALTER TABLE `rooms` ADD COLUMN `group_ref` int(11) unsigned GENERATED ALWAYS AS (NULLIF(`group_id`, 0)) STORED, ADD CONSTRAINT `fk_rooms_group_ref` FOREIGN KEY (`group_ref`) REFERENCES `groups` (`id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_statistics', 'groupid'), ('groups', 'id'))) = 2,
    'ALTER TABLE `user_statistics` ADD COLUMN `group_ref` int(11) unsigned GENERATED ALWAYS AS (NULLIF(`groupid`, 0)) STORED, ADD CONSTRAINT `fk_user_statistics_group_ref` FOREIGN KEY (`group_ref`) REFERENCES `groups` (`id`)', 'DO 0');
EXECUTE IMMEDIATE @migration_67;

-- 5. The keys.
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('bots', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `bots` ADD CONSTRAINT `fk_bots_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('bots_petdata', 'id'), ('bots', 'id'))) = 2,
    'ALTER TABLE `bots_petdata` ADD CONSTRAINT `fk_bots_petdata_id` FOREIGN KEY (`id`) REFERENCES `bots` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('bots_speech', 'bot_id'), ('bots', 'id'))) = 2,
    'ALTER TABLE `bots_speech` ADD CONSTRAINT `fk_bots_speech_bot_id` FOREIGN KEY (`bot_id`) REFERENCES `bots` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_accounts', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `camera_accounts` ADD CONSTRAINT `fk_camera_accounts_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_competition_entries', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `camera_competition_entries` ADD CONSTRAINT `fk_camera_competition_entries_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_media', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `camera_media` ADD CONSTRAINT `fk_camera_media_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_publications', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `camera_publications` ADD CONSTRAINT `fk_camera_publications_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_purchases', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `camera_purchases` ADD CONSTRAINT `fk_camera_purchases_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_purchases', 'item_id'), ('items', 'id'))) = 2,
    'ALTER TABLE `camera_purchases` ADD CONSTRAINT `fk_camera_purchases_item_id` FOREIGN KEY (`item_id`) REFERENCES `items` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('camera_quota', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `camera_quota` ADD CONSTRAINT `fk_camera_quota_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('catalog_marketplace_offers', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `catalog_marketplace_offers` ADD CONSTRAINT `fk_catalog_marketplace_offers_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('catalog_marketplace_offers', 'item_id'), ('furniture', 'id'))) = 2,
    'ALTER TABLE `catalog_marketplace_offers` ADD CONSTRAINT `fk_catalog_marketplace_offers_item_id` FOREIGN KEY (`item_id`) REFERENCES `furniture` (`id`) ON DELETE RESTRICT', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('club_credit_spending', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `club_credit_spending` ADD CONSTRAINT `fk_club_credit_spending_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('club_gift_claims', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `club_gift_claims` ADD CONSTRAINT `fk_club_gift_claims_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('club_membership_intervals', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `club_membership_intervals` ADD CONSTRAINT `fk_club_membership_intervals_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('club_paydays', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `club_paydays` ADD CONSTRAINT `fk_club_paydays_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('crafting_altars_recipes', 'altar_item_id'), ('furniture', 'id'))) = 2,
    'ALTER TABLE `crafting_altars_recipes` ADD CONSTRAINT `fk_crafting_altars_recipes_altar_item_id` FOREIGN KEY (`altar_item_id`) REFERENCES `furniture` (`id`) ON DELETE RESTRICT', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('crafting_recipes', 'reward_item_id'), ('furniture', 'id'))) = 2,
    'ALTER TABLE `crafting_recipes` ADD CONSTRAINT `fk_crafting_recipes_reward_item_id` FOREIGN KEY (`reward_item_id`) REFERENCES `furniture` (`id`) ON DELETE RESTRICT', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('crafting_recipes_ingredients', 'item_id'), ('furniture', 'id'))) = 2,
    'ALTER TABLE `crafting_recipes_ingredients` ADD CONSTRAINT `fk_crafting_recipes_ingredients_item_id` FOREIGN KEY (`item_id`) REFERENCES `furniture` (`id`) ON DELETE RESTRICT', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_forum_messages', 'author_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `group_forum_messages` ADD CONSTRAINT `fk_group_forum_messages_author_id` FOREIGN KEY (`author_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_forum_post_limits', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `group_forum_post_limits` ADD CONSTRAINT `fk_group_forum_post_limits_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_forum_read_markers', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `group_forum_read_markers` ADD CONSTRAINT `fk_group_forum_read_markers_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_forum_threads', 'author_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `group_forum_threads` ADD CONSTRAINT `fk_group_forum_threads_author_id` FOREIGN KEY (`author_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_memberships', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `group_memberships` ADD CONSTRAINT `fk_group_memberships_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_memberships', 'group_id'), ('groups', 'id'))) = 2,
    'ALTER TABLE `group_memberships` ADD CONSTRAINT `fk_group_memberships_group_id` FOREIGN KEY (`group_id`) REFERENCES `groups` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_requests', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `group_requests` ADD CONSTRAINT `fk_group_requests_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('group_requests', 'group_id'), ('groups', 'id'))) = 2,
    'ALTER TABLE `group_requests` ADD CONSTRAINT `fk_group_requests_group_id` FOREIGN KEY (`group_id`) REFERENCES `groups` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('groups', 'owner_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `groups` ADD CONSTRAINT `fk_groups_owner_id` FOREIGN KEY (`owner_id`) REFERENCES `users` (`id`) ON DELETE RESTRICT', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('items', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `items` ADD CONSTRAINT `fk_items_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('items', 'base_item'), ('furniture', 'id'))) = 2,
    'ALTER TABLE `items` ADD CONSTRAINT `fk_items_base_item` FOREIGN KEY (`base_item`) REFERENCES `furniture` (`id`) ON DELETE RESTRICT', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('items_groups', 'id'), ('items', 'id'))) = 2,
    'ALTER TABLE `items_groups` ADD CONSTRAINT `fk_items_groups_id` FOREIGN KEY (`id`) REFERENCES `items` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('items_groups', 'group_id'), ('groups', 'id'))) = 2,
    'ALTER TABLE `items_groups` ADD CONSTRAINT `fk_items_groups_group_id` FOREIGN KEY (`group_id`) REFERENCES `groups` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_friendships', 'user_one_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `messenger_friendships` ADD CONSTRAINT `fk_messenger_friendships_user_one_id` FOREIGN KEY (`user_one_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_friendships', 'user_two_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `messenger_friendships` ADD CONSTRAINT `fk_messenger_friendships_user_two_id` FOREIGN KEY (`user_two_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_offline_messages', 'to_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `messenger_offline_messages` ADD CONSTRAINT `fk_messenger_offline_messages_to_id` FOREIGN KEY (`to_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_offline_messages', 'from_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `messenger_offline_messages` ADD CONSTRAINT `fk_messenger_offline_messages_from_id` FOREIGN KEY (`from_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_requests', 'from_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `messenger_requests` ADD CONSTRAINT `fk_messenger_requests_from_id` FOREIGN KEY (`from_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('messenger_requests', 'to_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `messenger_requests` ADD CONSTRAINT `fk_messenger_requests_to_id` FOREIGN KEY (`to_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('moderation_preset_action_messages', 'parent_id'), ('moderation_preset_action_categories', 'id'))) = 2,
    'ALTER TABLE `moderation_preset_action_messages` ADD CONSTRAINT `fk_moderation_preset_action_messages_parent_id` FOREIGN KEY (`parent_id`) REFERENCES `moderation_preset_action_categories` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('moderation_topic_actions', 'parent_id'), ('moderation_topics', 'id'))) = 2,
    'ALTER TABLE `moderation_topic_actions` ADD CONSTRAINT `fk_moderation_topic_actions_parent_id` FOREIGN KEY (`parent_id`) REFERENCES `moderation_topics` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('navigator_publics', 'room_id'), ('rooms', 'id'))) = 2,
    'ALTER TABLE `navigator_publics` ADD CONSTRAINT `fk_navigator_publics_room_id` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('rcon_grants', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `rcon_grants` ADD CONSTRAINT `fk_rcon_grants_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('recycler_prizes', 'item_id'), ('furniture', 'id'))) = 2,
    'ALTER TABLE `recycler_prizes` ADD CONSTRAINT `fk_recycler_prizes_item_id` FOREIGN KEY (`item_id`) REFERENCES `furniture` (`id`) ON DELETE RESTRICT', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('recycler_prizes', 'level'), ('recycler_levels', 'level'))) = 2,
    'ALTER TABLE `recycler_prizes` ADD CONSTRAINT `fk_recycler_prizes_level` FOREIGN KEY (`level`) REFERENCES `recycler_levels` (`level`) ON DELETE RESTRICT', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('reward_track_prizes', 'track_id'), ('reward_tracks', 'id'))) = 2,
    'ALTER TABLE `reward_track_prizes` ADD CONSTRAINT `fk_reward_track_prizes_track_id` FOREIGN KEY (`track_id`) REFERENCES `reward_tracks` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('reward_track_tasks', 'track_id'), ('reward_tracks', 'id'))) = 2,
    'ALTER TABLE `reward_track_tasks` ADD CONSTRAINT `fk_reward_track_tasks_track_id` FOREIGN KEY (`track_id`) REFERENCES `reward_tracks` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_bans', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `room_bans` ADD CONSTRAINT `fk_room_bans_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_bans', 'room_id'), ('rooms', 'id'))) = 2,
    'ALTER TABLE `room_bans` ADD CONSTRAINT `fk_room_bans_room_id` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_filter', 'room_id'), ('rooms', 'id'))) = 2,
    'ALTER TABLE `room_filter` ADD CONSTRAINT `fk_room_filter_room_id` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_items_moodlight', 'item_id'), ('items', 'id'))) = 2,
    'ALTER TABLE `room_items_moodlight` ADD CONSTRAINT `fk_room_items_moodlight_item_id` FOREIGN KEY (`item_id`) REFERENCES `items` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_items_tele_links', 'tele_one_id'), ('items', 'id'))) = 2,
    'ALTER TABLE `room_items_tele_links` ADD CONSTRAINT `fk_room_items_tele_links_tele_one_id` FOREIGN KEY (`tele_one_id`) REFERENCES `items` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_items_tele_links', 'tele_two_id'), ('items', 'id'))) = 2,
    'ALTER TABLE `room_items_tele_links` ADD CONSTRAINT `fk_room_items_tele_links_tele_two_id` FOREIGN KEY (`tele_two_id`) REFERENCES `items` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_items_toner', 'id'), ('items', 'id'))) = 2,
    'ALTER TABLE `room_items_toner` ADD CONSTRAINT `fk_room_items_toner_id` FOREIGN KEY (`id`) REFERENCES `items` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_music_disc_definitions', 'base_item'), ('furniture', 'id'))) = 2,
    'ALTER TABLE `room_music_disc_definitions` ADD CONSTRAINT `fk_room_music_disc_definitions_base_item` FOREIGN KEY (`base_item`) REFERENCES `furniture` (`id`) ON DELETE RESTRICT', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_poll_questions', 'poll_id'), ('room_polls', 'id'))) = 2,
    'ALTER TABLE `room_poll_questions` ADD CONSTRAINT `fk_room_poll_questions_poll_id` FOREIGN KEY (`poll_id`) REFERENCES `room_polls` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_poll_responses', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `room_poll_responses` ADD CONSTRAINT `fk_room_poll_responses_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_poll_responses', 'poll_id'), ('room_polls', 'id'))) = 2,
    'ALTER TABLE `room_poll_responses` ADD CONSTRAINT `fk_room_poll_responses_poll_id` FOREIGN KEY (`poll_id`) REFERENCES `room_polls` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_polls', 'room_id'), ('rooms', 'id'))) = 2,
    'ALTER TABLE `room_polls` ADD CONSTRAINT `fk_room_polls_room_id` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_promotions', 'room_id'), ('rooms', 'id'))) = 2,
    'ALTER TABLE `room_promotions` ADD CONSTRAINT `fk_room_promotions_room_id` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_rights', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `room_rights` ADD CONSTRAINT `fk_room_rights_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_rights', 'room_id'), ('rooms', 'id'))) = 2,
    'ALTER TABLE `room_rights` ADD CONSTRAINT `fk_room_rights_room_id` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('room_wired_settings', 'room_id'), ('rooms', 'id'))) = 2,
    'ALTER TABLE `room_wired_settings` ADD CONSTRAINT `fk_room_wired_settings_room_id` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('rooms', 'owner'), ('users', 'id'))) = 2,
    'ALTER TABLE `rooms` ADD CONSTRAINT `fk_rooms_owner` FOREIGN KEY (`owner`) REFERENCES `users` (`id`) ON DELETE RESTRICT', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('rooms', 'model_name'), ('room_models', 'id'))) = 2,
    'ALTER TABLE `rooms` ADD CONSTRAINT `fk_rooms_model_name` FOREIGN KEY (`model_name`) REFERENCES `room_models` (`id`) ON DELETE RESTRICT', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('rooms', 'category'), ('navigator_categories', 'id'))) = 2,
    'ALTER TABLE `rooms` ADD CONSTRAINT `fk_rooms_category` FOREIGN KEY (`category`) REFERENCES `navigator_categories` (`id`) ON DELETE RESTRICT', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('safety_quiz_questions', 'quiz_code'), ('safety_quizzes', 'code'))) = 2,
    'ALTER TABLE `safety_quiz_questions` ADD CONSTRAINT `fk_safety_quiz_questions_quiz_code` FOREIGN KEY (`quiz_code`) REFERENCES `safety_quizzes` (`code`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('server_reward_logs', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `server_reward_logs` ADD CONSTRAINT `fk_server_reward_logs_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('server_reward_logs', 'reward_id'), ('server_rewards', 'id'))) = 2,
    'ALTER TABLE `server_reward_logs` ADD CONSTRAINT `fk_server_reward_logs_reward_id` FOREIGN KEY (`reward_id`) REFERENCES `server_rewards` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('snowwar_game_tokens', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `snowwar_game_tokens` ADD CONSTRAINT `fk_snowwar_game_tokens_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('snowwar_scores', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `snowwar_scores` ADD CONSTRAINT `fk_snowwar_scores_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_access_tokens', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_access_tokens` ADD CONSTRAINT `fk_user_access_tokens_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_access_tokens', 'session_id'), ('user_sessions', 'id'))) = 2,
    'ALTER TABLE `user_access_tokens` ADD CONSTRAINT `fk_user_access_tokens_session_id` FOREIGN KEY (`session_id`) REFERENCES `user_sessions` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_achievements', 'userid'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_achievements` ADD CONSTRAINT `fk_user_achievements_userid` FOREIGN KEY (`userid`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_badges', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_badges` ADD CONSTRAINT `fk_user_badges_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_calendar_claims', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_calendar_claims` ADD CONSTRAINT `fk_user_calendar_claims_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_calendar_claims', 'campaign_id'), ('campaign_calendars', 'id'))) = 2,
    'ALTER TABLE `user_calendar_claims` ADD CONSTRAINT `fk_user_calendar_claims_campaign_id` FOREIGN KEY (`campaign_id`) REFERENCES `campaign_calendars` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_calendar_claims', 'reward_id'), ('campaign_calendar_rewards', 'id'))) = 2,
    'ALTER TABLE `user_calendar_claims` ADD CONSTRAINT `fk_user_calendar_claims_reward_id` FOREIGN KEY (`reward_id`) REFERENCES `campaign_calendar_rewards` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_clothing', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_clothing` ADD CONSTRAINT `fk_user_clothing_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_club_memberships', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_club_memberships` ADD CONSTRAINT `fk_user_club_memberships_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_crafting_recipes', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_crafting_recipes` ADD CONSTRAINT `fk_user_crafting_recipes_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_effects', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_effects` ADD CONSTRAINT `fk_user_effects_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_favorites', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_favorites` ADD CONSTRAINT `fk_user_favorites_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_favorites', 'room_id'), ('rooms', 'id'))) = 2,
    'ALTER TABLE `user_favorites` ADD CONSTRAINT `fk_user_favorites_room_id` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_ignores', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_ignores` ADD CONSTRAINT `fk_user_ignores_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_ignores', 'ignore_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_ignores` ADD CONSTRAINT `fk_user_ignores_ignore_id` FOREIGN KEY (`ignore_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_info', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_info` ADD CONSTRAINT `fk_user_info_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_permissions', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_permissions` ADD CONSTRAINT `fk_user_permissions_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_permissions', 'granted_by'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_permissions` ADD CONSTRAINT `fk_user_permissions_granted_by` FOREIGN KEY (`granted_by`) REFERENCES `users` (`id`) ON DELETE SET NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_presents', 'item_id'), ('items', 'id'))) = 2,
    'ALTER TABLE `user_presents` ADD CONSTRAINT `fk_user_presents_item_id` FOREIGN KEY (`item_id`) REFERENCES `items` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_presents', 'base_id'), ('furniture', 'id'))) = 2,
    'ALTER TABLE `user_presents` ADD CONSTRAINT `fk_user_presents_base_id` FOREIGN KEY (`base_id`) REFERENCES `furniture` (`id`) ON DELETE RESTRICT', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_quests', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_quests` ADD CONSTRAINT `fk_user_quests_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_quests', 'quest_id'), ('quests', 'id'))) = 2,
    'ALTER TABLE `user_quests` ADD CONSTRAINT `fk_user_quests_quest_id` FOREIGN KEY (`quest_id`) REFERENCES `quests` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_recycler', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_recycler` ADD CONSTRAINT `fk_user_recycler_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_remember_tokens', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_remember_tokens` ADD CONSTRAINT `fk_user_remember_tokens_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_remember_tokens', 'family_id'), ('user_sessions', 'id'))) = 2,
    'ALTER TABLE `user_remember_tokens` ADD CONSTRAINT `fk_user_remember_tokens_family_id` FOREIGN KEY (`family_id`) REFERENCES `user_sessions` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_roles', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_roles` ADD CONSTRAINT `fk_user_roles_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_roles', 'granted_by'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_roles` ADD CONSTRAINT `fk_user_roles_granted_by` FOREIGN KEY (`granted_by`) REFERENCES `users` (`id`) ON DELETE SET NULL', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_roomvisits', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_roomvisits` ADD CONSTRAINT `fk_user_roomvisits_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_roomvisits', 'room_id'), ('rooms', 'id'))) = 2,
    'ALTER TABLE `user_roomvisits` ADD CONSTRAINT `fk_user_roomvisits_room_id` FOREIGN KEY (`room_id`) REFERENCES `rooms` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_safety_quizzes', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_safety_quizzes` ADD CONSTRAINT `fk_user_safety_quizzes_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_safety_quizzes', 'quiz_code'), ('safety_quizzes', 'code'))) = 2,
    'ALTER TABLE `user_safety_quizzes` ADD CONSTRAINT `fk_user_safety_quizzes_quiz_code` FOREIGN KEY (`quiz_code`) REFERENCES `safety_quizzes` (`code`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_saved_searches', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_saved_searches` ADD CONSTRAINT `fk_user_saved_searches_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_sessions', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_sessions` ADD CONSTRAINT `fk_user_sessions_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_statistics', 'id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_statistics` ADD CONSTRAINT `fk_user_statistics_id` FOREIGN KEY (`id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_talent_rewards', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_talent_rewards` ADD CONSTRAINT `fk_user_talent_rewards_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_vouchers', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_vouchers` ADD CONSTRAINT `fk_user_vouchers_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_vouchers', 'voucher'), ('catalog_vouchers', 'voucher'))) = 2,
    'ALTER TABLE `user_vouchers` ADD CONSTRAINT `fk_user_vouchers_voucher` FOREIGN KEY (`voucher`) REFERENCES `catalog_vouchers` (`voucher`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('user_wardrobe', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `user_wardrobe` ADD CONSTRAINT `fk_user_wardrobe_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('users_habbicons', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `users_habbicons` ADD CONSTRAINT `fk_users_habbicons_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('users_reward_track_prizes', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `users_reward_track_prizes` ADD CONSTRAINT `fk_users_reward_track_prizes_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('users_reward_track_tasks', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `users_reward_track_tasks` ADD CONSTRAINT `fk_users_reward_track_tasks_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('users_reward_tracks', 'user_id'), ('users', 'id'))) = 2,
    'ALTER TABLE `users_reward_tracks` ADD CONSTRAINT `fk_users_reward_tracks_user_id` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('users_reward_tracks', 'track_id'), ('reward_tracks', 'id'))) = 2,
    'ALTER TABLE `users_reward_tracks` ADD CONSTRAINT `fk_users_reward_tracks_track_id` FOREIGN KEY (`track_id`) REFERENCES `reward_tracks` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('wired_item_configurations', 'item_id'), ('items', 'id'))) = 2,
    'ALTER TABLE `wired_item_configurations` ADD CONSTRAINT `fk_wired_item_configurations_item_id` FOREIGN KEY (`item_id`) REFERENCES `items` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('wired_items', 'id'), ('items', 'id'))) = 2,
    'ALTER TABLE `wired_items` ADD CONSTRAINT `fk_wired_items_id` FOREIGN KEY (`id`) REFERENCES `items` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('wired_reward_state', 'item_id'), ('items', 'id'))) = 2,
    'ALTER TABLE `wired_reward_state` ADD CONSTRAINT `fk_wired_reward_state_item_id` FOREIGN KEY (`item_id`) REFERENCES `items` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;
SET @migration_67 = IF((SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (TABLE_NAME, COLUMN_NAME) IN (('wired_variable_values', 'definition_id'), ('items', 'id'))) = 2,
    'ALTER TABLE `wired_variable_values` ADD CONSTRAINT `fk_wired_variable_values_definition_id` FOREIGN KEY (`definition_id`) REFERENCES `items` (`id`) ON DELETE CASCADE', 'DO 0');
EXECUTE IMMEDIATE @migration_67;

SET @migration_67 = NULL;
