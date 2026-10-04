-- Apply once while PlusEMU is stopped, after update 21. Back up first; DDL is not transactional.
-- Roles replace both staff ranks and VIP tiers. users.rank is only a security-level cache.
SET time_zone = '+00:00';

-- Pre-flight uses temporary data only. Stop on the first SQL error (do not use --force).
-- A failed assertion leaves all legacy tables and rows untouched, even if CHECK enforcement is disabled.
CREATE TEMPORARY TABLE acl_migration_preflight (
 assertion VARCHAR(100) NOT NULL,
 valid BOOLEAN NULL
);
INSERT INTO acl_migration_preflight SELECT 'update_21_schema_required',
 COUNT(*) = 14 FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name IN
 ('permissions_groups','permissions','permissions_rights','permissions_commands','permissions_subscriptions',
  'users','subscriptions','ranks','catalog_pages','navigator_categories','catalog_admin_log','room_chat_styles','room_models','server_settings');
INSERT INTO acl_migration_preflight SELECT 'migration_22_must_not_have_started',
 COUNT(*) = 0 FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name IN
 ('roles','acl_permissions','role_permissions','user_roles','user_permissions','role_limits','acl_audit_log');
INSERT INTO acl_migration_preflight SELECT 'migration_22_columns_must_not_exist', COUNT(*) = 0
FROM information_schema.columns WHERE table_schema = DATABASE() AND
 ((table_name IN ('catalog_pages','navigator_categories','room_chat_styles','room_models') AND column_name = 'required_permission')
 OR (table_name = 'room_chat_styles' AND column_name IN ('requires_hc','enabled'))
 OR (table_name = 'room_models' AND column_name = 'required_club_level'));
INSERT INTO acl_migration_preflight SELECT 'valid_rank_ids_and_weight_range', NOT EXISTS (
 SELECT id FROM permissions_groups WHERE id < 1 OR id > 214748364
 UNION ALL SELECT rank FROM users WHERE rank IS NULL OR rank < 1 OR rank > 214748364
 UNION ALL SELECT min_rank FROM catalog_pages WHERE min_rank < 0 OR min_rank > 214748364
 UNION ALL SELECT required_rank FROM navigator_categories WHERE required_rank < 0 OR required_rank > 214748364
 UNION ALL SELECT group_id FROM permissions_commands WHERE group_id < 0 OR group_id > 214748364
);
INSERT INTO acl_migration_preflight SELECT 'valid_vip_tiers', NOT EXISTS (
 SELECT id FROM subscriptions WHERE id < 0
 UNION ALL SELECT rank_vip FROM users WHERE rank_vip < 0
 UNION ALL SELECT min_vip FROM catalog_pages WHERE min_vip < 0
 UNION ALL SELECT subscription_id FROM permissions_subscriptions WHERE subscription_id < 1
 UNION ALL SELECT subscription_id FROM permissions_commands WHERE subscription_id < 0
);
-- The shipped seed includes a permission_id = 0 placeholder; it has no effective grant.
INSERT INTO acl_migration_preflight SELECT 'rights_reference_existing_groups_and_permissions', NOT EXISTS (
 SELECT 1 FROM permissions_rights r LEFT JOIN permissions p ON p.id = r.permission_id
 LEFT JOIN permissions_groups g ON g.id = r.group_id WHERE (p.id IS NULL AND r.permission_id <> 0) OR g.id IS NULL
 UNION ALL SELECT 1 FROM permissions_subscriptions s LEFT JOIN permissions p ON p.id = s.permission_id WHERE p.id IS NULL AND s.permission_id <> 0
);
INSERT INTO acl_migration_preflight SELECT 'metadata_fits_new_columns', NOT EXISTS (
 SELECT 1 FROM permissions_groups WHERE CHAR_LENGTH(name) > 100 OR CHAR_LENGTH(description) > 255 OR CHAR_LENGTH(badge_code) > 64
 UNION ALL SELECT 1 FROM subscriptions WHERE CHAR_LENGTH(name) > 100 OR CHAR_LENGTH(badge_code) > 64
 UNION ALL SELECT 1 FROM permissions WHERE CHAR_LENGTH(description) > 255 OR CHAR_LENGTH(permission) > 184
  OR permission NOT REGEXP '^[a-zA-Z0-9_.]+$'
 UNION ALL SELECT 1 FROM permissions_commands WHERE command NOT REGEXP '^command_[a-zA-Z0-9_]+$' OR CHAR_LENGTH(command) > 184
);
INSERT INTO acl_migration_preflight SELECT 'catalog_undo_thresholds_are_valid', NOT EXISTS (
 SELECT 1 FROM (
  SELECT before_json AS snapshot FROM catalog_admin_log WHERE entity_type = 'PAGE' AND before_json IS NOT NULL AND JSON_VALID(before_json)
  UNION ALL SELECT after_json FROM catalog_admin_log WHERE entity_type = 'PAGE' AND after_json IS NOT NULL AND JSON_VALID(after_json)
 ) snapshots WHERE COALESCE(JSON_VALUE(snapshot, IF(JSON_CONTAINS_PATH(snapshot, 'one', '$.page'), '$.page.minRank', '$.minRank')), 1)
 NOT REGEXP '^[0-9]+$'
 OR CAST(COALESCE(JSON_VALUE(snapshot, IF(JSON_CONTAINS_PATH(snapshot, 'one', '$.page'), '$.page.minRank', '$.minRank')), 1) AS UNSIGNED) > 214748364
);

-- A duplicate PRIMARY KEY aborts independently of server CHECK settings/version.
CREATE TEMPORARY TABLE acl_migration_guard (valid BOOLEAN NOT NULL PRIMARY KEY);
INSERT INTO acl_migration_guard VALUES (TRUE);
INSERT INTO acl_migration_guard SELECT TRUE WHERE EXISTS
 (SELECT 1 FROM acl_migration_preflight WHERE valid IS NULL OR valid = FALSE);

-- Allocate every rank role before changing source data. Reserve VIP/default names,
-- synthetic rank_N names and suffixes ending in digits for unambiguous _<id> disambiguation.
CREATE TEMPORARY TABLE acl_migrated_rank_roles (
 id INT NOT NULL PRIMARY KEY,
 slug VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 name VARCHAR(100) NOT NULL,
 description VARCHAR(255) NOT NULL,
 badge_code VARCHAR(64) NOT NULL,
 synthetic BOOLEAN NOT NULL
);
INSERT INTO acl_migrated_rank_roles
SELECT id, TRIM(BOTH '_' FROM REGEXP_REPLACE(LOWER(name), '[^a-z0-9]+', '_')),
 name, description, badge_code, FALSE FROM permissions_groups;
INSERT IGNORE INTO acl_migrated_rank_roles
SELECT rank_id, CONCAT('rank_', rank_id), CONCAT('Rank ', rank_id),
 'Unlisted legacy rank or permission gate threshold retained during migration.', '', TRUE
FROM (
 SELECT rank AS rank_id FROM users
 UNION SELECT min_rank FROM catalog_pages WHERE min_rank > 1
 UNION SELECT required_rank FROM navigator_categories WHERE required_rank > 1
 UNION SELECT CAST(COALESCE(JSON_VALUE(snapshot, IF(JSON_CONTAINS_PATH(snapshot, 'one', '$.page'), '$.page.minRank', '$.minRank')), 1) AS UNSIGNED)
 FROM (
  SELECT before_json AS snapshot FROM catalog_admin_log WHERE entity_type = 'PAGE' AND before_json IS NOT NULL AND JSON_VALID(before_json)
  UNION ALL SELECT after_json FROM catalog_admin_log WHERE entity_type = 'PAGE' AND after_json IS NOT NULL AND JSON_VALID(after_json)
 ) snapshots
 UNION SELECT 1
) ranks_to_retain WHERE rank_id >= 1;
CREATE TEMPORARY TABLE acl_rank_slug_counts AS
SELECT slug, COUNT(*) AS occurrences FROM acl_migrated_rank_roles GROUP BY slug;
UPDATE acl_migrated_rank_roles r JOIN acl_rank_slug_counts c ON c.slug = r.slug
SET r.slug = CASE
 WHEN r.id = 1 THEN 'default'
 WHEN r.synthetic OR r.slug = '' THEN CONCAT('rank_', r.id)
 -- vip_<id> itself is reserved for tiers, so a rank named VIP needs a second suffix.
 WHEN r.slug = 'vip' THEN CONCAT('vip_', r.id, '_', r.id)
 WHEN c.occurrences > 1 OR r.slug IN ('default','vip','gold_vip','events_staff') OR r.slug REGEXP '_[0-9]+$'
 THEN CONCAT(LEFT(r.slug, 80), '_', r.id)
 ELSE r.slug END;
INSERT INTO acl_migration_preflight SELECT 'rank_slugs_are_unique', COUNT(*) = COUNT(DISTINCT slug) FROM acl_migrated_rank_roles;
INSERT INTO acl_migration_guard SELECT TRUE WHERE EXISTS
 (SELECT 1 FROM acl_migration_preflight WHERE valid IS NULL OR valid = FALSE);
ALTER TABLE acl_migrated_rank_roles ADD UNIQUE KEY (slug);
DROP TEMPORARY TABLE acl_migration_preflight, acl_rank_slug_counts, acl_migration_guard;

-- The removed custom soundboard feature must not survive as an orphaned grant.
DELETE rights_row FROM permissions_rights rights_row JOIN permissions p ON p.id = rights_row.permission_id
WHERE p.permission = 'acc_soundboard_manage' OR p.permission LIKE 'soundboard%';
DELETE subscription_row FROM permissions_subscriptions subscription_row JOIN permissions p ON p.id = subscription_row.permission_id
WHERE p.permission = 'acc_soundboard_manage' OR p.permission LIKE 'soundboard%';
DELETE FROM permissions WHERE permission = 'acc_soundboard_manage' OR permission LIKE 'soundboard%';

CREATE TABLE roles (
 id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
 slug VARCHAR(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL UNIQUE,
 name VARCHAR(100) NOT NULL,
 description VARCHAR(255) NOT NULL DEFAULT '',
 weight INT NOT NULL DEFAULT 0,
 security_level TINYINT UNSIGNED NOT NULL DEFAULT 1,
 badge_code VARCHAR(64) NOT NULL DEFAULT '',
 is_staff BOOLEAN NOT NULL DEFAULT FALSE,
 is_hidden BOOLEAN NOT NULL DEFAULT FALSE,
 created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
 CHECK (security_level <= 7)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE acl_permissions (
 `key` VARCHAR(191) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 category VARCHAR(64) NOT NULL,
 description VARCHAR(255) NOT NULL DEFAULT '',
 is_orphan BOOLEAN NOT NULL DEFAULT FALSE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE role_permissions (
 role_id INT NOT NULL,
 permission_key VARCHAR(191) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 PRIMARY KEY (role_id, permission_key),
 FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE CASCADE
) ENGINE=InnoDB;
CREATE TABLE user_roles (
 user_id INT NOT NULL,
 role_id INT NOT NULL,
 granted_by INT NULL,
 expires_at DATETIME(6) NULL,
 created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 PRIMARY KEY (user_id, role_id),
 KEY expires_at (expires_at),
 FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE CASCADE
) ENGINE=InnoDB;
CREATE TABLE user_permissions (
 user_id INT NOT NULL,
 permission_key VARCHAR(191) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 effect ENUM('grant','deny') NOT NULL,
 granted_by INT NULL,
 reason VARCHAR(512) NOT NULL DEFAULT '',
 expires_at DATETIME(6) NULL,
 created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 PRIMARY KEY (user_id, permission_key),
 KEY expires_at (expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE role_limits (
 role_id INT NOT NULL,
 limit_key VARCHAR(191) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 value INT NOT NULL,
 PRIMARY KEY (role_id, limit_key),
 FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE CASCADE
) ENGINE=InnoDB;
CREATE TABLE acl_audit_log (
 id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
 actor_id INT NULL,
 action VARCHAR(64) NOT NULL,
 target_type VARCHAR(32) NOT NULL,
 target_id INT NOT NULL,
 payload JSON NOT NULL,
 created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
 KEY target (target_type, target_id),
 KEY actor_created (actor_id, created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TEMPORARY TABLE acl_key_map (
 old_key VARCHAR(191) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 new_key VARCHAR(191) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 PRIMARY KEY (old_key, new_key)
);
INSERT INTO acl_key_map VALUES
('acc_catalogfurni','catalog.edit'),
('acc_furni_delete','furni.delete'),
('acc_furnidata_edit','furni.edit'),
('acc_housekeeping','housekeeping.access'),
('bot_edit_any_override','bot.edit_any_override'),
('bot_place_any_override','bot.place_any_override'),
('command_hotel_alert','command.ha'),
('command_override_massenable','command.override_massenable'),
('events_staff','staff.events'),
('fuse_group_accept_any','group.accept.any'),
('group_delete_limit_override','group.delete_limit_override'),
('group_delete_override','group.delete_override'),
('group_management_override','group.management_override'),
('housekeeping_alert','housekeeping.alert'),
('housekeeping_economy','housekeeping.economy'),
('housekeeping_password','housekeeping.password'),
('housekeeping_private_data','housekeeping.private_data'),
('housekeeping_rank','housekeeping.roles.manage'),
('housekeeping_room_ownership','housekeeping.room_ownership'),
('housekeeping_rooms','housekeeping.rooms'),
('housekeeping_sanction','housekeeping.sanction'),
('mod_alert','moderation.alert'),
('mod_ban_any','moderation.ban'),
('mod_caution','moderation.caution'),
('mod_disconnect_any','moderation.disconnect_any'),
('mod_ip_ban','moderation.ip_ban'),
('mod_kick','moderation.kick'),
('mod_kick_any','moderation.kick_any'),
('mod_machine_ban','moderation.machine_ban'),
('mod_make_say_any','moderation.make_say_any'),
('mod_mute','moderation.mute'),
('mod_mute_any','moderation.mute.any'),
('mod_mute_limit_override','moderation.mute_limit_override'),
('mod_room_alert','moderation.room_alert'),
('mod_soft_ban','moderation.ban.soft'),
('mod_tickets','moderation.tickets'),
('mod_tool','moderation.tool'),
('mod_trade_lock','moderation.trade_lock'),
('mod_trade_lock_any','moderation.trade_lock_any'),
('override_command_setmax_limit','room.user_limit.override'),
('room_any_owner','room.owner.any'),
('room_any_rights','room.rights.any'),
('room_ban_override','room.ban_override'),
('room_delete_any','room.delete_any'),
('room_enter_full','room.enter_full'),
('room_enter_locked','room.enter_locked'),
('room_ignore_mute','room.ignore_mute'),
('room_item_place_exchange_anywhere','room.item_place_exchange_anywhere'),
('room_item_save_branding_items','room.item_save_branding_items'),
('room_item_take','room.item_take'),
('room_item_use_any_stack_tile','room.item_use_any_stack_tile'),
('room_item_wired_rewards','room.item_wired_rewards'),
('room_override_custom_config','room.override_custom_config'),
('room_trade_override','room.trade_override'),
('room_unload_any','room.unload_any'),
('room_whisper_override','room.whisper_override'),
('staff_ignore_advertisement_reports','staff.ignore_advertisement_reports'),
('staff_ignore_mod_alert','staff.ignore_mod_alert'),
('word_filter_override','chat.filter_bypass'),
('acc_supporttool','moderation.tool'),
('acc_anyroomowner','room.owner.any'),
('acc_ambassador','ambassador'),
('acc_calendar_force','campaign.calendar.force'),
('acc_camera','camera.use'),
('acc_closedice_room','room.dice.close_any'),
('acc_rewardtrack','rewardtrack.manage'),
('acc_staff_pick','navigator.staff_pick'),
('acc_wheeladmin','fortune_wheel.manage'),
('command_delete_group','command.deletegroup'),
('command_carry','command.carry'),
('command_bubble','command.bubble'),
('command_update','command.update'),
('command_hal','command.hal'),
('command_event_alert','command.eha'),
('command_setspeed','command.setspeed'),
('command_ejectall','command.ejectall'),
('command_sit','command.sit'),
('command_mute_bots','command.mutebots'),
('command_stand','command.stand'),
('command_flagme','command.flagme'),
('command_info','command.about'),
('command_sell_room','command.sellroom'),
('command_unload','command.unload'),
('command_lay','command.lay'),
('command_kickpets','command.kickpets'),
('command_empty_items','command.emptyitems'),
('command_disable_diagonal','command.disablediagonal'),
('command_regen_maps','command.regenmaps'),
('command_disable_gifts','command.disablegifts'),
('command_convert_credits','command.convertcredits'),
('command_setmax','command.setmax'),
('command_mute_pets','command.mutepets'),
('command_stats','command.stats'),
('command_room','command.room'),
('command_pickall','command.pickall'),
('command_disable_mimic','command.disablemimic'),
('command_dnd','command.dnd'),
('command_kickbots','command.kickbots'),
('command_disable_whispers','command.disablewhispers'),
('command_give','command.give'),
('command_unmute','command.unmute'),
('command_mip','command.mip'),
('command_alert_user','command.alert'),
('command_goto','command.goto'),
('command_staff_alert','command.stress'),
('command_staff_alert','command.sa'),
('command_roommute','command.roommute'),
('command_forced_effects','command.forced_effects'),
('command_unroommute','command.roomunmute'),
('command_trade_ban','command.tradeban'),
('command_ip_ban','command.ipban'),
('command_ignore_whispers','command.ignorewhispers'),
('command_user_info','command.userinfo'),
('command_ban','command.ban'),
('command_kick','command.kick'),
('command_room_kick','command.roomkick'),
('command_disconnect','command.dc'),
('command_give_badge','command.givebadge'),
('command_room_badge','command.roombadge'),
('command_mass_badge','command.massbadge'),
('command_flaguser','command.flaguser'),
('command_room_alert','command.roomalert'),
('command_mute','command.mute'),
('command_follow','command.follow'),
('command_mimic','command.mimic'),
('command_dance','command.dance'),
('command_faceless','command.faceless'),
('command_pet','command.pet'),
('command_pull','command.pull'),
('command_moonwalk','command.moonwalk'),
('command_super_push','command.spush'),
('command_push','command.push'),
('command_enable','command.enable'),
('command_allaroundme','command.allaroundme'),
('command_summon','command.summon'),
('command_freeze','command.freeze'),
('command_super_pull','command.spull'),
('command_massdance','command.massdance'),
('command_override','command.override'),
('command_massenable','command.massenable'),
('command_fastwalk','command.fastwalk'),
('command_coords','command.coords'),
('command_alleyesonme','command.alleyesonme'),
('command_super_fastwalk','command.superfastwalk'),
('command_makesay','command.makesay'),
('command_forcesit','command.forcesit'),
('command_unfreeze','command.unfreeze'),
('command_teleport','command.teleport');
CREATE TEMPORARY TABLE acl_command_map LIKE acl_key_map;
INSERT INTO acl_command_map VALUES
('command_delete_group','command.deletegroup'),
('command_carry','command.carry'),
('command_bubble','command.bubble'),
('command_update','command.update'),
('command_hal','command.hal'),
('command_event_alert','command.eha'),
('command_setspeed','command.setspeed'),
('command_ejectall','command.ejectall'),
('command_sit','command.sit'),
('command_mute_bots','command.mutebots'),
('command_stand','command.stand'),
('command_flagme','command.flagme'),
('command_info','command.about'),
('command_sell_room','command.sellroom'),
('command_unload','command.unload'),
('command_lay','command.lay'),
('command_kickpets','command.kickpets'),
('command_empty_items','command.emptyitems'),
('command_disable_diagonal','command.disablediagonal'),
('command_regen_maps','command.regenmaps'),
('command_disable_gifts','command.disablegifts'),
('command_convert_credits','command.convertcredits'),
('command_setmax','command.setmax'),
('command_mute_pets','command.mutepets'),
('command_stats','command.stats'),
('command_room','command.room'),
('command_pickall','command.pickall'),
('command_disable_mimic','command.disablemimic'),
('command_dnd','command.dnd'),
('command_kickbots','command.kickbots'),
('command_disable_whispers','command.disablewhispers'),
('command_give','command.give'),
('command_unmute','command.unmute'),
('command_mip','command.mip'),
('command_alert_user','command.alert'),
('command_goto','command.goto'),
('command_staff_alert','command.stress'),
('command_staff_alert','command.sa'),
('command_roommute','command.roommute'),
('command_forced_effects','command.forced_effects'),
('command_unroommute','command.roomunmute'),
('command_trade_ban','command.tradeban'),
('command_ip_ban','command.ipban'),
('command_ignore_whispers','command.ignorewhispers'),
('command_user_info','command.userinfo'),
('command_ban','command.ban'),
('command_hotel_alert','command.ha'),
('command_kick','command.kick'),
('command_room_kick','command.roomkick'),
('command_disconnect','command.dc'),
('command_give_badge','command.givebadge'),
('command_room_badge','command.roombadge'),
('command_mass_badge','command.massbadge'),
('command_flaguser','command.flaguser'),
('command_room_alert','command.roomalert'),
('command_mute','command.mute'),
('command_follow','command.follow'),
('command_mimic','command.mimic'),
('command_dance','command.dance'),
('command_faceless','command.faceless'),
('command_pet','command.pet'),
('command_pull','command.pull'),
('command_moonwalk','command.moonwalk'),
('command_super_push','command.spush'),
('command_push','command.push'),
('command_enable','command.enable'),
('command_allaroundme','command.allaroundme'),
('command_summon','command.summon'),
('command_freeze','command.freeze'),
('command_super_pull','command.spull'),
('command_massdance','command.massdance'),
('command_override','command.override'),
('command_massenable','command.massenable'),
('command_fastwalk','command.fastwalk'),
('command_coords','command.coords'),
('command_alleyesonme','command.alleyesonme'),
('command_super_fastwalk','command.superfastwalk'),
('command_makesay','command.makesay'),
('command_forcesit','command.forcesit'),
('command_unfreeze','command.unfreeze'),
('command_teleport','command.teleport');

-- This unused command-row name also named a separate rights-only privilege.
-- Keep its command metadata orphaned rather than granting that privilege by rank.
INSERT INTO acl_command_map VALUES ('command_override_massenable', 'command.unused_override_massenable');

-- Keep operator-defined permissions as orphans, so their data is visible for reconciliation.
INSERT IGNORE INTO acl_key_map
SELECT permission, IF(INSTR(permission, '.') > 0, permission,
 CASE WHEN permission LIKE 'mod\_%' THEN CONCAT('moderation.', SUBSTRING(permission, 5))
      WHEN permission LIKE 'command\_%' THEN CONCAT('command.', SUBSTRING(permission, 9))
      WHEN permission LIKE 'room\_%' THEN CONCAT('room.', SUBSTRING(permission, 6))
      WHEN permission LIKE 'group\_%' THEN CONCAT('group.', SUBSTRING(permission, 7))
      WHEN permission LIKE 'bot\_%' THEN CONCAT('bot.', SUBSTRING(permission, 5))
      WHEN permission LIKE 'staff\_%' THEN CONCAT('staff.', SUBSTRING(permission, 7))
      ELSE CONCAT('legacy.', permission) END)
FROM permissions p WHERE permission NOT IN ('silver_vip', 'gold_vip')
 AND NOT EXISTS (SELECT 1 FROM acl_key_map m WHERE m.old_key = p.permission);
INSERT IGNORE INTO acl_command_map
SELECT command, CONCAT('command.', SUBSTRING(command, 9)) FROM permissions_commands c
WHERE NOT EXISTS (SELECT 1 FROM acl_command_map m WHERE m.old_key = c.command);
INSERT IGNORE INTO acl_permissions (`key`, category, description, is_orphan)
SELECT m.new_key, SUBSTRING_INDEX(m.new_key, '.', 1), MAX(p.description), TRUE
FROM permissions p JOIN acl_key_map m ON m.old_key = p.permission GROUP BY m.new_key;
INSERT IGNORE INTO acl_permissions (`key`, category, description, is_orphan)
SELECT new_key, 'command', 'Migrated chat command.', TRUE FROM acl_command_map;

-- Migrate retained catalog undo snapshots, including the page nested in MOVE entries.
CREATE TEMPORARY TABLE acl_page_snapshots AS
SELECT id, source, snapshot,
 IF(JSON_CONTAINS_PATH(snapshot, 'one', '$.page'), '$.page', '$') AS page_path
FROM (
 SELECT id, 'before' AS source, before_json AS snapshot FROM catalog_admin_log WHERE entity_type = 'PAGE' AND before_json IS NOT NULL AND JSON_VALID(before_json)
 UNION ALL
 SELECT id, 'after', after_json FROM catalog_admin_log WHERE entity_type = 'PAGE' AND after_json IS NOT NULL AND JSON_VALID(after_json)
) AS snapshots;
ALTER TABLE acl_page_snapshots ADD min_rank INT NOT NULL DEFAULT 1, ADD min_vip INT NOT NULL DEFAULT 0,
 ADD required_permission VARCHAR(191) CHARACTER SET ascii COLLATE ascii_bin NULL;
UPDATE acl_page_snapshots SET
 min_rank = COALESCE(JSON_VALUE(snapshot, CONCAT(page_path, '.minRank')), 1),
 min_vip = IF(JSON_VALUE(snapshot, CONCAT(page_path, '.vipOnly')) IN ('true', '1'), 1, 0);

-- The original id is retained for each rank role. Rank 1 becomes the implicit default role.
INSERT INTO roles (id, slug, name, description, weight, security_level, badge_code, is_staff)
SELECT id, slug, name, description, IF(id = 1, 0, id * 10), LEAST(7, id), badge_code, id > 1
FROM acl_migrated_rank_roles ORDER BY id;
INSERT IGNORE INTO roles (slug, name, description, weight, security_level)
VALUES ('default', 'User', 'Default access for every user.', 0, 1);
DROP TEMPORARY TABLE acl_migrated_rank_roles;
CREATE TEMPORARY TABLE acl_rank_roles (old_rank INT PRIMARY KEY, role_id INT NOT NULL);
INSERT INTO acl_rank_roles SELECT id, id FROM roles;
INSERT IGNORE INTO role_permissions
SELECT pr.group_id, m.new_key FROM permissions_rights pr
JOIN permissions p ON p.id = pr.permission_id
JOIN acl_key_map m ON m.old_key = p.permission
JOIN roles r ON r.id = pr.group_id;
INSERT INTO user_roles (user_id, role_id) SELECT u.id, r.role_id FROM users u JOIN acl_rank_roles r ON r.old_rank = u.rank;

CREATE TEMPORARY TABLE acl_vip_tiers (tier INT NOT NULL PRIMARY KEY, role_id INT NULL);
INSERT IGNORE INTO acl_vip_tiers (tier)
SELECT id FROM subscriptions WHERE id > 0 UNION SELECT subscription_id FROM permissions_subscriptions WHERE subscription_id > 0
UNION SELECT rank_vip FROM users WHERE rank_vip > 0 UNION SELECT subscription_id FROM permissions_commands WHERE subscription_id > 0
UNION SELECT min_vip FROM catalog_pages WHERE min_vip > 0 UNION SELECT 1;
INSERT INTO roles (slug, name, description, weight, security_level, badge_code)
SELECT CASE v.tier WHEN 1 THEN 'vip' WHEN 2 THEN 'gold_vip' WHEN 3 THEN 'events_staff' ELSE CONCAT('vip_', v.tier) END,
 COALESCE(s.name, CONCAT('VIP ', v.tier)), 'Migrated subscription tier.', 10, 1, COALESCE(s.badge_code, '')
FROM acl_vip_tiers v LEFT JOIN subscriptions s ON s.id = v.tier ORDER BY v.tier;
UPDATE acl_vip_tiers v JOIN roles r
ON r.slug = CASE v.tier WHEN 1 THEN 'vip' WHEN 2 THEN 'gold_vip' WHEN 3 THEN 'events_staff' ELSE CONCAT('vip_', v.tier) END
SET v.role_id = r.id;
INSERT IGNORE INTO role_permissions
SELECT v.role_id, m.new_key FROM permissions_subscriptions ps
JOIN acl_vip_tiers v ON v.tier = ps.subscription_id
JOIN permissions p ON p.id = ps.permission_id JOIN acl_key_map m ON m.old_key = p.permission;
INSERT INTO user_roles (user_id, role_id)
SELECT u.id, v.role_id FROM users u JOIN acl_vip_tiers v ON v.tier = u.rank_vip;

-- Commands become explicit grants. Staff bypass of VIP-only commands is intentional.
INSERT IGNORE INTO role_permissions
SELECT r.role_id, m.new_key FROM permissions_commands c
JOIN acl_command_map m ON m.old_key = c.command
JOIN acl_rank_roles r ON r.old_rank >= c.group_id
WHERE c.subscription_id <= 0 OR r.old_rank > 1;
INSERT IGNORE INTO role_permissions
SELECT v.role_id, m.new_key FROM permissions_commands c
JOIN acl_command_map m ON m.old_key = c.command
JOIN acl_vip_tiers v ON v.tier >= c.subscription_id
WHERE c.group_id <= 1 AND c.subscription_id > 0;

INSERT IGNORE INTO acl_permissions (`key`, category, description) VALUES
 ('ambassador','community','Ambassador access.'),
 ('room.owner.any','room','Act as any room owner.'),
 ('camera.use','camera','Use the camera.'),
 ('navigator.room_models.staff','navigator','Use staff room models.'),
 ('navigator.categories.staff','navigator','See staff navigator categories.'),
 ('navigator.events.moderate','navigator','Moderate navigator events.'),
 ('navigator.staff_pick','navigator','Manage staff picks.'),
 ('catalog.gift.staff','catalog','Staff catalog gifts.'),
 ('room.youtube.control_any','room','Control room videos.'),
 ('chat.style.staff','chat','Use staff chat styles.'),
 ('staff.receive_alerts','staff','Receive staff broadcasts.'),
 ('chat.report.unlimited','chat','Bypass chat report cooldown.'),
 ('avatar.name.staff_prefix_required','avatar','Require the staff username prefix.');
INSERT IGNORE INTO role_permissions
SELECT role_id, 'staff.receive_alerts' FROM acl_rank_roles WHERE old_rank >= 2;
INSERT IGNORE INTO role_permissions
SELECT role_id, 'chat.report.unlimited' FROM acl_rank_roles WHERE old_rank >= 2;
INSERT IGNORE INTO role_permissions
SELECT role_id, 'avatar.name.staff_prefix_required' FROM acl_rank_roles WHERE old_rank IN (2,3);
INSERT IGNORE INTO role_permissions
SELECT role_id, 'navigator.room_models.staff' FROM acl_rank_roles WHERE old_rank >= 4;
INSERT IGNORE INTO role_permissions
SELECT role_id, 'navigator.categories.staff' FROM acl_rank_roles WHERE old_rank >= 7;
INSERT IGNORE INTO role_permissions
SELECT role_id, 'navigator.events.moderate' FROM acl_rank_roles WHERE old_rank >= 5;
INSERT IGNORE INTO role_permissions
SELECT role_id, 'catalog.gift.staff' FROM acl_rank_roles WHERE old_rank >= 5;
INSERT IGNORE INTO role_permissions
SELECT role_id, 'navigator.staff_pick' FROM acl_rank_roles WHERE old_rank >= 7;
INSERT IGNORE INTO role_permissions
SELECT role_id, 'room.youtube.control_any' FROM acl_rank_roles WHERE old_rank >= 4;
INSERT IGNORE INTO role_permissions
SELECT role_id, 'chat.style.staff' FROM acl_rank_roles WHERE old_rank >= 5;
INSERT IGNORE INTO role_permissions
SELECT role_id, 'chat.style.staff' FROM role_permissions WHERE permission_key = 'moderation.tool';
INSERT IGNORE INTO role_permissions
SELECT role_id, 'room.owner.any' FROM acl_rank_roles WHERE old_rank >= 4;
INSERT IGNORE INTO user_permissions (user_id, permission_key, effect, reason)
SELECT id, 'ambassador', 'grant', 'Migrated ambassador status.' FROM users WHERE is_ambassador = TRUE;

-- The camera setting remains authoritative; translate an operator-configured old key.
UPDATE server_settings s JOIN acl_key_map m ON m.old_key = s.value
SET s.value = m.new_key WHERE s.`key` = 'camera.permission';
INSERT IGNORE INTO role_permissions
SELECT r.id, 'camera.use' FROM roles r
WHERE (SELECT value FROM server_settings WHERE `key` = 'camera.permission') = '0';
INSERT IGNORE INTO role_permissions
SELECT rp.role_id, 'camera.use' FROM role_permissions rp
WHERE rp.permission_key = (SELECT value FROM server_settings WHERE `key` = 'camera.permission');
DELETE FROM server_settings WHERE `key` = 'camera.permission';

UPDATE acl_page_snapshots s JOIN roles r ON r.id = s.min_rank
SET s.required_permission = CONCAT('catalog.pages.', r.slug) WHERE s.min_rank > 1;
UPDATE acl_page_snapshots s JOIN roles r ON r.slug = 'vip'
SET s.required_permission = CONCAT('catalog.pages.', r.slug) WHERE s.min_rank <= 1 AND s.min_vip > 0;
INSERT IGNORE INTO acl_permissions (`key`, category, description)
SELECT DISTINCT required_permission, 'catalog', 'Migrated catalog page access.' FROM acl_page_snapshots WHERE required_permission IS NOT NULL;
INSERT IGNORE INTO role_permissions
SELECT rr.role_id, s.required_permission FROM acl_page_snapshots s JOIN acl_rank_roles rr
ON rr.old_rank >= s.min_rank AND (s.min_vip <= 0 OR rr.old_rank > 1) WHERE s.required_permission IS NOT NULL;
INSERT IGNORE INTO role_permissions
SELECT v.role_id, s.required_permission FROM acl_page_snapshots s JOIN acl_vip_tiers v ON v.tier >= s.min_vip
WHERE s.min_rank <= 1 AND s.min_vip > 0 AND s.required_permission IS NOT NULL;
UPDATE acl_page_snapshots SET snapshot = JSON_REMOVE(
 JSON_SET(snapshot, CONCAT(page_path, '.requiredPermission'), COALESCE(required_permission, '')),
 CONCAT(page_path, '.minRank'), CONCAT(page_path, '.vipOnly'));
UPDATE catalog_admin_log l JOIN acl_page_snapshots s ON s.id = l.id AND s.source = 'before' SET l.before_json = s.snapshot;
UPDATE catalog_admin_log l JOIN acl_page_snapshots s ON s.id = l.id AND s.source = 'after' SET l.after_json = s.snapshot;
DROP TEMPORARY TABLE acl_page_snapshots;

-- Rank-gated catalog pages are staff pages. Legacy staff already bypassed min_vip.
ALTER TABLE catalog_pages ADD required_permission VARCHAR(191) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER min_vip;
UPDATE catalog_pages p JOIN roles r ON r.id = p.min_rank
SET p.required_permission = CONCAT('catalog.pages.', r.slug) WHERE p.min_rank > 1;
UPDATE catalog_pages p JOIN acl_vip_tiers v ON v.tier = p.min_vip JOIN roles r ON r.id = v.role_id
SET p.required_permission = CONCAT('catalog.pages.', r.slug) WHERE p.min_rank <= 1 AND p.min_vip > 0;
INSERT IGNORE INTO acl_permissions (`key`, category, description)
SELECT DISTINCT required_permission, 'catalog', 'Migrated catalog page access.' FROM catalog_pages WHERE required_permission IS NOT NULL;
INSERT IGNORE INTO role_permissions
SELECT rr.role_id, p.required_permission FROM catalog_pages p JOIN acl_rank_roles rr
ON rr.old_rank >= p.min_rank AND (p.min_vip <= 0 OR rr.old_rank > 1)
WHERE p.required_permission IS NOT NULL;
INSERT IGNORE INTO role_permissions
SELECT v.role_id, p.required_permission FROM catalog_pages p JOIN acl_vip_tiers v ON v.tier >= p.min_vip
WHERE p.min_rank <= 1 AND p.min_vip > 0 AND p.required_permission IS NOT NULL;

-- Navigator gates retain their original rank thresholds as explicit named-role grants.
ALTER TABLE navigator_categories ADD required_permission VARCHAR(191) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER required_rank;
UPDATE navigator_categories n JOIN roles r ON r.id = n.required_rank
SET n.required_permission = CONCAT('navigator.searches.', r.slug) WHERE n.required_rank > 1;
INSERT IGNORE INTO acl_permissions (`key`, category, description)
SELECT DISTINCT required_permission, 'navigator', 'Migrated navigator search access.' FROM navigator_categories WHERE required_permission IS NOT NULL;
INSERT IGNORE INTO role_permissions
SELECT rr.role_id, n.required_permission FROM navigator_categories n
JOIN acl_rank_roles rr ON rr.old_rank >= n.required_rank WHERE n.required_permission IS NOT NULL;
ALTER TABLE navigator_categories DROP required_rank;

ALTER TABLE badge_definitions MODIFY required_right VARCHAR(191) NOT NULL DEFAULT '';
ALTER TABLE room_chat_styles CHANGE required_right required_permission VARCHAR(191) NULL DEFAULT '',
 ADD COLUMN IF NOT EXISTS requires_hc BOOLEAN NOT NULL DEFAULT FALSE,
 ADD COLUMN IF NOT EXISTS enabled BOOLEAN NOT NULL DEFAULT TRUE;
UPDATE badge_definitions b JOIN acl_key_map m ON m.old_key = b.required_right SET b.required_right = m.new_key;
UPDATE room_chat_styles b JOIN acl_key_map m ON m.old_key = b.required_permission SET b.required_permission = m.new_key;
UPDATE room_chat_styles SET required_permission = 'chat.style.staff' WHERE required_permission = 'moderation.tool';

-- Default picker metadata is authoritative on the server. Preserve custom permission gates
-- and existing enabled choices; stock empty/moderation gates follow the known style list.
INSERT IGNORE INTO room_chat_styles (id, name, required_permission, requires_hc, enabled) VALUES
 (0, '', NULL, FALSE, TRUE),
 (1, '', NULL, FALSE, TRUE),
 (2, '', NULL, FALSE, TRUE),
 (3, '', NULL, FALSE, TRUE),
 (4, '', NULL, FALSE, TRUE),
 (5, '', NULL, FALSE, TRUE),
 (6, '', NULL, FALSE, TRUE),
 (7, '', NULL, FALSE, TRUE),
 (8, '', NULL, FALSE, TRUE),
 (9, '', NULL, FALSE, TRUE),
 (10, '', NULL, FALSE, TRUE),
 (11, '', NULL, FALSE, TRUE),
 (12, '', NULL, FALSE, TRUE),
 (13, '', NULL, FALSE, TRUE),
 (14, '', NULL, FALSE, TRUE),
 (15, '', NULL, FALSE, TRUE),
 (16, '', NULL, FALSE, TRUE),
 (17, '', NULL, FALSE, TRUE),
 (18, '', NULL, FALSE, TRUE),
 (19, '', NULL, FALSE, TRUE),
 (20, '', NULL, FALSE, TRUE),
 (21, '', NULL, FALSE, TRUE),
 (22, '', NULL, FALSE, TRUE),
 (23, '', NULL, FALSE, TRUE),
 (24, '', NULL, FALSE, TRUE),
 (25, '', NULL, FALSE, TRUE),
 (26, '', NULL, FALSE, TRUE),
 (27, '', NULL, FALSE, TRUE),
 (28, '', NULL, FALSE, TRUE),
 (29, '', NULL, FALSE, TRUE),
 (30, '', NULL, FALSE, TRUE),
 (31, '', NULL, FALSE, TRUE),
 (32, '', NULL, FALSE, TRUE),
 (33, '', NULL, FALSE, TRUE),
 (34, '', NULL, FALSE, TRUE),
 (35, '', NULL, FALSE, TRUE),
 (36, '', NULL, FALSE, TRUE),
 (37, '', NULL, FALSE, TRUE),
 (38, '', NULL, FALSE, TRUE),
 (39, '', NULL, FALSE, TRUE),
 (40, '', NULL, FALSE, TRUE),
 (41, '', NULL, FALSE, TRUE),
 (42, '', NULL, FALSE, TRUE),
 (43, '', NULL, FALSE, TRUE),
 (44, '', NULL, FALSE, TRUE),
 (45, '', NULL, FALSE, TRUE),
 (46, '', NULL, FALSE, TRUE),
 (47, '', NULL, FALSE, TRUE),
 (48, '', NULL, FALSE, TRUE),
 (49, '', NULL, FALSE, TRUE),
 (50, '', NULL, FALSE, TRUE),
 (51, '', NULL, FALSE, TRUE),
 (52, '', NULL, FALSE, TRUE),
 (53, '', NULL, FALSE, TRUE);
UPDATE room_chat_styles SET required_permission = CASE WHEN id IN (1,2,8,23,30,31,33,34,37,39,40,41,42,43,44,45,46,47,48,49,50,51,52,53) THEN 'chat.style.staff' ELSE NULL END
WHERE id BETWEEN 0 AND 53 AND COALESCE(required_permission, '') IN ('', 'chat.style.staff');
UPDATE room_chat_styles SET requires_hc = id IN (9,10,11,12,13,14,15,16,17,18,19,20,21,22,24,25,26,27,28,29,32,35,36,38) WHERE id BETWEEN 0 AND 53;

-- Move model requirements into the authoritative server schema. Stock model levels
-- match the known client list; custom models retain the legacy club-only fallback.
SET @acl_model_club_exists = (SELECT COUNT(*) FROM information_schema.COLUMNS
 WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'room_models' AND COLUMN_NAME = 'required_club_level');
ALTER TABLE room_models
 ADD COLUMN IF NOT EXISTS required_club_level INT NOT NULL DEFAULT 0 AFTER club_only,
 ADD COLUMN IF NOT EXISTS required_permission VARCHAR(191) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER required_club_level;
UPDATE room_models SET required_club_level = IF(club_only = '1', 1, 0) WHERE @acl_model_club_exists = 0;
UPDATE room_models SET required_club_level = 0 WHERE id IN ('model_a','model_b','model_c','model_d','model_e','model_f','model_i','model_j','model_k','model_l','model_m','model_n');
UPDATE room_models SET required_club_level = 1 WHERE id IN ('model_g','model_h','model_o','model_p','model_q','model_r','model_u','model_v');
UPDATE room_models SET required_club_level = 2 WHERE id IN ('model_t','model_w','model_x','model_y','model_z','model_0','model_1','model_2','model_3','model_4','model_5','model_6','model_7','model_8','model_9');
ALTER TABLE room_models DROP club_only;

-- Numeric benefits resolve by maximum over active roles.
INSERT INTO role_limits
SELECT role_id, 'limit.daily_respects', IF(old_rank = 1, 10, 20) FROM acl_rank_roles;
INSERT INTO role_limits
SELECT role_id, 'limit.daily_pet_respects', IF(old_rank = 1, 10, 20) FROM acl_rank_roles;
INSERT INTO role_limits
SELECT role_id, 'limit.name_change_frequency', 0 FROM acl_rank_roles;
UPDATE role_limits l JOIN role_permissions rp ON rp.role_id = l.role_id AND rp.permission_key = 'moderation.tool'
SET l.value = 604800 WHERE l.limit_key = 'limit.name_change_frequency';
INSERT INTO role_limits
SELECT v.role_id, 'limit.daily_respects', IF(v.tier = 1, 15, 20) FROM acl_vip_tiers v;
INSERT INTO role_limits
SELECT v.role_id, 'limit.daily_pet_respects', IF(v.tier = 1, 15, 20) FROM acl_vip_tiers v;
INSERT INTO role_limits
SELECT role_id, 'limit.name_change_frequency', CASE tier WHEN 1 THEN 1 WHEN 2 THEN 7 ELSE 604800 END FROM acl_vip_tiers;
INSERT INTO role_limits
SELECT v.role_id, 'limit.currency_credits', s.credits FROM acl_vip_tiers v JOIN subscriptions s ON s.id = v.tier;
INSERT INTO role_limits
SELECT v.role_id, 'limit.currency_duckets', s.duckets FROM acl_vip_tiers v JOIN subscriptions s ON s.id = v.tier;
INSERT INTO role_limits
SELECT pr.group_id, 'limit.staff_effect', MAX(CASE p.permission WHEN 'silver_vip' THEN 23 WHEN 'gold_vip' THEN 178 ELSE 187 END)
FROM permissions_rights pr JOIN permissions p ON p.id = pr.permission_id
WHERE p.permission IN ('silver_vip','gold_vip','events_staff') GROUP BY pr.group_id;
INSERT INTO role_limits
SELECT v.role_id, 'limit.staff_effect', MAX(CASE p.permission WHEN 'silver_vip' THEN 23 WHEN 'gold_vip' THEN 178 ELSE 187 END)
FROM permissions_subscriptions ps JOIN permissions p ON p.id = ps.permission_id JOIN acl_vip_tiers v ON v.tier = ps.subscription_id
WHERE p.permission IN ('silver_vip','gold_vip','events_staff') GROUP BY v.role_id;
INSERT INTO role_limits
SELECT role_id, 'limit.flood_tolerance', 1 FROM acl_rank_roles;
INSERT INTO role_limits
SELECT v.role_id, 'limit.flood_tolerance', MAX(CASE p.permission WHEN 'silver_vip' THEN 11 WHEN 'gold_vip' THEN 14 ELSE 18 END)
FROM permissions_subscriptions ps JOIN permissions p ON p.id = ps.permission_id JOIN acl_vip_tiers v ON v.tier = ps.subscription_id
WHERE p.permission IN ('silver_vip','gold_vip','events_staff') GROUP BY v.role_id;
INSERT INTO role_limits
SELECT pr.group_id, 'limit.flood_tolerance', MAX(CASE p.permission WHEN 'silver_vip' THEN 11 WHEN 'gold_vip' THEN 14 ELSE 18 END)
FROM permissions_rights pr JOIN permissions p ON p.id = pr.permission_id
WHERE p.permission IN ('silver_vip','gold_vip','events_staff') GROUP BY pr.group_id
ON DUPLICATE KEY UPDATE value = GREATEST(value, VALUES(value));

-- Implicit default role must not introduce custom rank-1 rights to existing staff users.
INSERT IGNORE INTO user_permissions (user_id, permission_key, effect, reason)
SELECT u.id, d.permission_key, 'deny', 'Preserve non-inherited legacy rank rights.'
FROM users u JOIN roles dr ON dr.slug = 'default' JOIN role_permissions d ON d.role_id = dr.id
JOIN acl_key_map m ON m.new_key = d.permission_key
WHERE NOT EXISTS (SELECT 1 FROM user_roles ur JOIN role_permissions rp ON rp.role_id = ur.role_id
 WHERE ur.user_id = u.id AND rp.permission_key = d.permission_key);

UPDATE users u SET u.rank = COALESCE((SELECT MAX(r.security_level) FROM user_roles ur JOIN roles r ON r.id = ur.role_id
WHERE ur.user_id = u.id AND (ur.expires_at IS NULL OR ur.expires_at > UTC_TIMESTAMP(6))), 1);
INSERT INTO acl_audit_log (actor_id, action, target_type, target_id, payload)
SELECT NULL, 'migration.role', 'user', user_id, JSON_OBJECT('roleId', role_id, 'expiresAt', expires_at) FROM user_roles;
INSERT INTO acl_audit_log (actor_id, action, target_type, target_id, payload)
SELECT NULL, 'migration.override', 'user', user_id, JSON_OBJECT('permission', permission_key, 'effect', effect) FROM user_permissions;

ALTER TABLE catalog_pages DROP min_rank, DROP min_vip;
ALTER TABLE users DROP rank_vip, DROP is_ambassador;
DROP TABLE permissions_rights, permissions_subscriptions, permissions_commands, permissions_groups, permissions, ranks, subscriptions;
DROP TEMPORARY TABLE acl_key_map, acl_command_map, acl_rank_roles, acl_vip_tiers;
