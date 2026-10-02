-- Apply while PlusEMU is stopped; SQL updates are not automatic.
-- Camera media can only be minted by the emulator's authenticated renderer.
CREATE TABLE IF NOT EXISTS camera_media (
 id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 user_id INT NOT NULL, room_id INT UNSIGNED NOT NULL,
 created_at DATETIME(6) NOT NULL,
 KEY owner_created (user_id, created_at)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS camera_purchases (
 item_id INT UNSIGNED NOT NULL PRIMARY KEY,
 media_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 user_id INT NOT NULL, created_at DATETIME(6) NOT NULL,
 KEY media_id (media_id)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS camera_publications (
 media_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 user_id INT NOT NULL, room_id INT UNSIGNED NOT NULL,
 created_at DATETIME(6) NOT NULL,
 KEY owner_created (user_id, created_at)
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS camera_accounts (
 user_id INT NOT NULL PRIMARY KEY,
 last_publish_at DATETIME(6) NULL
) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS camera_competition_entries (
 media_id CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
 user_id INT NOT NULL, created_at DATETIME(6) NOT NULL,
 KEY owner_created (user_id, created_at)
) ENGINE=InnoDB;
INSERT INTO furniture
 (item_name,public_name,type,sprite_id,can_stack,allow_recycle,allow_trade,
  allow_marketplace_sell,allow_gift,allow_inventory_stack,interaction_type)
SELECT 'external_image_wallitem_poster_small','Photograph','i',4597,'0','0','1','0','1','0','camera_picture'
WHERE NOT EXISTS (SELECT 1 FROM furniture WHERE interaction_type='camera_picture');

-- UTC quotas survive reconnects; rows are locked when reserving a render.
CREATE TABLE IF NOT EXISTS camera_quota (
 user_id INT NOT NULL, quota_date DATE NOT NULL,
 captures INT UNSIGNED NOT NULL DEFAULT 0, edits INT UNSIGNED NOT NULL DEFAULT 0,
 last_capture_at DATETIME(6) NULL, last_edit_at DATETIME(6) NULL,
 last_thumbnail_at DATETIME(6) NULL,
 PRIMARY KEY (user_id, quota_date)
) ENGINE=InnoDB;

-- Existing operator choices are preserved, including an intentionally disabled camera.
INSERT IGNORE INTO server_settings (`key`, `value`, description) VALUES
 ('camera.enabled','1','Enable trusted room photography'),
 ('camera.price.credits','2','Credits charged per wall photograph'),
 ('camera.price.points','0','Activity points charged per wall photograph'),
 ('camera.price.points.type','0','Photo point currency: 0 duckets (other types fail closed)'),
 ('camera.price.publish.points','1','Duckets charged per publication'),
 ('camera.price.publish.points.type','0','Publication point currency: 0 duckets (other types fail closed)'),
 ('camera.publish.cooldown','180','Seconds between publications per account'),
 ('camera.render.cooldown','5','Seconds between new shutter captures'),
 ('camera.render.daily','50','New shutter captures per account per UTC day'),
 ('camera.render.edit.cooldown','1','Seconds between editor renders'),
 ('camera.render.edit.daily','200','Editor renders per account per UTC day'),
 ('camera.thumbnail.cooldown','15','Seconds between room thumbnail captures'),
 ('camera.competition.enabled','0','Enable photo competition submissions'),
 ('camera.competition.daily','3','Competition entries per account per UTC day'),
 ('camera.competition.require_email','0','Require an email for competition entries'),
 ('camera.permission','0','0 allows room occupants, otherwise a lowercase right name');
INSERT IGNORE INTO server_settings (`key`, `value`, description)
SELECT 'camera.item_id', CAST(MIN(id) AS CHAR), 'Wall camera_picture furniture definition'
FROM furniture WHERE type='i' AND interaction_type='camera_picture';

-- Each threshold is incremental because achievement progress resets on unlocking.
INSERT INTO achievements (group_name, category, level, reward_pixels, reward_points, progress_needed, game_id)
SELECT 'ACH_CameraPhotoCount', 'explore', levels.level, 0, 5, levels.progress_needed, 0
FROM (SELECT 1 AS level, 1 AS progress_needed UNION ALL SELECT 2,4
 UNION ALL SELECT 3,5 UNION ALL SELECT 4,10 UNION ALL SELECT 5,10
 UNION ALL SELECT 6,20 UNION ALL SELECT 7,25 UNION ALL SELECT 8,25
 UNION ALL SELECT 9,50 UNION ALL SELECT 10,50) AS levels
WHERE NOT EXISTS (SELECT 1 FROM achievements existing
 WHERE existing.group_name='ACH_CameraPhotoCount' AND existing.level=levels.level);
