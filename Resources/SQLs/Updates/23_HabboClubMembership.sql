-- Stop the emulator and apply after 22_RoleBasedAccessControl.sql.
ALTER TABLE catalog_club_offers DROP COLUMN type;
ALTER TABLE user_club_memberships MODIFY expires_at BIGINT NOT NULL,
 ADD started_at BIGINT NOT NULL DEFAULT 0,
 ADD first_started_at BIGINT NOT NULL DEFAULT 0,
 ADD past_seconds BIGINT NOT NULL DEFAULT 0,
 ADD modified_at BIGINT NOT NULL DEFAULT 0,
 ADD gifts_claimed INT NOT NULL DEFAULT 0;
-- Old rows recorded only expiry. Preserve their time without inventing past tenure.
UPDATE user_club_memberships SET started_at = LEAST(UNIX_TIMESTAMP(), expires_at),
 first_started_at = LEAST(UNIX_TIMESTAMP(), expires_at), modified_at = UNIX_TIMESTAMP() WHERE expires_at > 0;
INSERT INTO acl_permissions (`key`, category, description, is_orphan)
 VALUES ('club.access', 'club', 'Complimentary Habbo Club access.', 0);
INSERT IGNORE INTO role_permissions (role_id, permission_key)
 SELECT id, 'club.access' FROM roles WHERE is_staff = 1;
ALTER TABLE catalog_pages ADD required_club_level INT NOT NULL DEFAULT 0;
-- These permissions were generated solely from the removed min_vip column.
UPDATE catalog_pages p JOIN roles r ON p.required_permission = CONCAT('catalog.pages.', r.slug)
 SET p.required_club_level = 2, p.required_permission = NULL WHERE r.slug IN ('vip', 'gold_vip', 'events_staff') OR r.slug REGEXP '^vip_[0-9]+$';
UPDATE catalog_pages SET page_layout = 'vip_buy' WHERE id = 5 AND page_link = 'habbo_club';
UPDATE catalog_pages SET page_layout = 'vip_buy', page_link = 'hc_membership' WHERE id = 7 AND caption = 'Buy Club';
CREATE TABLE club_gift_offers (
 catalog_item_id INT NOT NULL PRIMARY KEY, days_required INT NOT NULL DEFAULT 0,
 enabled TINYINT(1) NOT NULL DEFAULT 1
) ENGINE=InnoDB;
INSERT IGNORE INTO club_gift_offers (catalog_item_id, days_required)
 SELECT i.id, IF(i.extradata REGEXP '^[0-9]{1,6}$', CAST(i.extradata AS UNSIGNED), 0)
 FROM catalog_items i JOIN catalog_pages p ON p.id = i.page_id WHERE p.page_layout IN ('club_gift', 'club_gifts');
-- The previous hard-coded gift already exists in the original catalog.
INSERT IGNORE INTO club_gift_offers (catalog_item_id) SELECT id FROM catalog_items WHERE catalog_name = 'hc_arab_chair';
CREATE TABLE club_gift_claims (
 id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, user_id INT NOT NULL, gift_number INT NOT NULL,
 catalog_item_id INT NOT NULL, claimed_at BIGINT NOT NULL, UNIQUE KEY (user_id, gift_number)
) ENGINE=InnoDB;
CREATE TABLE club_credit_spending (
 id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, user_id INT NOT NULL, credits INT NOT NULL,
 spent_at BIGINT NOT NULL, KEY (user_id, spent_at)
) ENGINE=InnoDB;
CREATE TABLE club_paydays (
 user_id INT NOT NULL, payday BIGINT NOT NULL, spent INT NOT NULL, streak_bonus INT NOT NULL,
 spending_bonus INT NOT NULL, paid TINYINT(1) NOT NULL, PRIMARY KEY(user_id, payday)
) ENGINE=InnoDB;
INSERT IGNORE INTO server_settings (`key`, `value`, `description`) VALUES
 ('club.limit.rooms.normal', '50', 'Habbo Club policy.'), ('club.limit.rooms.member', '100', 'Habbo Club policy.'),
 ('club.limit.friends.normal', '300', 'Habbo Club policy.'), ('club.limit.friends.member', '800', 'Habbo Club policy.'),
 ('club.limit.visitors.normal', '50', 'Habbo Club policy.'), ('club.limit.visitors.member', '75', 'Habbo Club policy.'),
 ('club.payday.percentage', '10', 'Habbo Club policy.');
-- Registration configuration now defaults to no roles and no club.
ALTER TABLE users ALTER vip SET DEFAULT '0';

CREATE TABLE club_membership_intervals (
 user_id INT NOT NULL, started_at BIGINT NOT NULL, expires_at BIGINT NOT NULL,
 PRIMARY KEY (user_id, started_at)
) ENGINE=InnoDB;
INSERT INTO club_membership_intervals SELECT user_id, started_at, expires_at FROM user_club_memberships WHERE started_at > 0;
