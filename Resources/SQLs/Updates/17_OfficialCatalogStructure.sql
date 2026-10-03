-- Apply while PlusEMU is stopped; SQL updates are not automatic. Written for MariaDB.
-- Room for the official catalog: long page names, per-offer club level, preview image and order,
-- separate builders club pages and typed front page items.
ALTER TABLE catalog_pages
 MODIFY caption VARCHAR(128) NOT NULL,
 MODIFY page_link VARCHAR(128) NOT NULL DEFAULT '',
 MODIFY page_layout VARCHAR(64) NOT NULL DEFAULT 'default_3x3',
 ADD COLUMN IF NOT EXISTS catalog_mode ENUM('NORMAL','BUILDERS_CLUB') NOT NULL DEFAULT 'NORMAL';
ALTER TABLE catalog_items
 MODIFY badge VARCHAR(64) NOT NULL DEFAULT '',
 MODIFY extradata VARCHAR(1024) NOT NULL DEFAULT '',
 ADD COLUMN IF NOT EXISTS club_level TINYINT UNSIGNED NOT NULL DEFAULT 0,
 ADD COLUMN IF NOT EXISTS preview_image VARCHAR(255) NOT NULL DEFAULT '',
 ADD COLUMN IF NOT EXISTS order_num INT NOT NULL DEFAULT 0;
ALTER TABLE catalog_promotions
 MODIFY title VARCHAR(128) DEFAULT '',
 MODIFY image VARCHAR(255) DEFAULT '',
 MODIFY page_link VARCHAR(128) DEFAULT '',
 ADD COLUMN IF NOT EXISTS position INT NOT NULL DEFAULT 0,
 ADD COLUMN IF NOT EXISTS item_type TINYINT NOT NULL DEFAULT 0,
 ADD COLUMN IF NOT EXISTS offer_id INT NOT NULL DEFAULT -1,
 ADD COLUMN IF NOT EXISTS product_code VARCHAR(128) NOT NULL DEFAULT '',
 ADD COLUMN IF NOT EXISTS expires_at INT NOT NULL DEFAULT 0;
-- Existing promotions keep the slot they had: their id.
UPDATE catalog_promotions SET position = id WHERE position = 0;
