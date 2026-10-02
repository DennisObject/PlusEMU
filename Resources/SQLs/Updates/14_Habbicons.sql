-- Apply while PlusEMU is stopped. Back up the database first; Plus does not auto-run SQL updates.
-- Existing configured collections, prices and ownership are preserved when this script is rerun.
-- Wallet row locks require InnoDB (the original Plus users table is already InnoDB).
SET @wallet_engine = (SELECT ENGINE FROM information_schema.TABLES
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'users');
SET @wallet_ddl = IF(@wallet_engine <> 'InnoDB', 'ALTER TABLE users ENGINE=InnoDB', 'SELECT 1');
PREPARE wallet_ddl FROM @wallet_ddl;
EXECUTE wallet_ddl;
DEALLOCATE PREPARE wallet_ddl;

CREATE TABLE IF NOT EXISTS habbicon_collections (
    id INT NOT NULL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    reward_id INT NOT NULL DEFAULT 0,
    cost_credits INT UNSIGNED NOT NULL DEFAULT 0,
    cost_points INT UNSIGNED NOT NULL DEFAULT 0,
    points_type INT UNSIGNED NOT NULL DEFAULT 0
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS habbicons (
    id INT NOT NULL PRIMARY KEY,
    collection_id INT NOT NULL,
    name VARCHAR(100) NOT NULL,
    cost_credits INT UNSIGNED NOT NULL DEFAULT 0,
    cost_points INT UNSIGNED NOT NULL DEFAULT 0,
    points_type INT UNSIGNED NOT NULL DEFAULT 0,
    available BOOLEAN NOT NULL DEFAULT TRUE,
    default_owned BOOLEAN NOT NULL DEFAULT FALSE,
    KEY collection_id (collection_id),
    FOREIGN KEY (collection_id) REFERENCES habbicon_collections(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS users_habbicons (
    user_id INT NOT NULL,
    habbicon_id INT NOT NULL,
    state TINYINT NOT NULL DEFAULT 2,
    unseen BOOLEAN NOT NULL DEFAULT FALSE,
    last_used BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, habbicon_id),
    KEY recent (user_id, last_used),
    FOREIGN KEY (habbicon_id) REFERENCES habbicons(id),
    CHECK (state IN (1, 2, 3))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Collection membership and rewards are hotel configuration; AIR supplies them over the wire.
-- Names and collection badge ids come from the September Habbicon asset set.
INSERT IGNORE INTO habbicon_collections (id, name, reward_id, cost_credits) VALUES
    (7, 'duck', 38, 40), (8, 'duck2', 49, 40), (5, 'frank', 60, 40), (6, 'toast', 71, 40);

INSERT IGNORE INTO habbicons (id, collection_id, name, default_owned, cost_credits) VALUES
    (28, 7, 'duck_duck', 1, 0),
    (29, 7, 'duck_happy', 0, 5),
    (30, 7, 'duck_sad', 0, 5),
    (31, 7, 'duck_shock', 0, 5),
    (32, 7, 'duck_think', 0, 5),
    (33, 7, 'duck_nohear', 0, 5),
    (34, 7, 'duck_nosee', 0, 5),
    (35, 7, 'duck_nosay', 0, 5),
    (36, 7, 'duck_angel', 0, 5),
    (37, 7, 'duck_devil', 0, 5),
    (38, 7, 'duck_spinning', 0, 0),
    (39, 8, 'duck_cool', 0, 5),
    (40, 8, 'duck_pleased', 0, 5),
    (41, 8, 'duck_laughing', 0, 5),
    (42, 8, 'duck_grimace', 0, 5),
    (43, 8, 'duck_devious', 0, 5),
    (44, 8, 'duck_metal', 0, 5),
    (45, 8, 'duck_pleading', 0, 5),
    (46, 8, 'duck_silly', 0, 5),
    (47, 8, 'duck_wink', 0, 5),
    (48, 8, 'duck_party', 0, 5),
    (49, 8, 'duck_love', 0, 0),
    (50, 5, 'frank_frank', 0, 5),
    (51, 5, 'frank_smile', 0, 5),
    (52, 5, 'frank_happy', 0, 5),
    (53, 5, 'frank_sad', 0, 5),
    (54, 5, 'frank_scared', 0, 5),
    (55, 5, 'frank_surprised', 0, 5),
    (56, 5, 'frank_thinking', 0, 5),
    (57, 5, 'frank_silly', 0, 5),
    (58, 5, 'frank_relief', 0, 5),
    (59, 5, 'frank_wink', 0, 5),
    (60, 5, 'frank_stareyes', 0, 0),
    (61, 6, 'toast_toast', 0, 5),
    (62, 6, 'toast_happy', 0, 5),
    (63, 6, 'toast_cute', 0, 5),
    (64, 6, 'toast_wink', 0, 5),
    (65, 6, 'toast_sad', 0, 5),
    (66, 6, 'toast_cry', 0, 5),
    (67, 6, 'toast_grumpy', 0, 5),
    (68, 6, 'toast_sleep', 0, 5),
    (69, 6, 'toast_shock', 0, 5),
    (70, 6, 'toast_flustered', 0, 5),
    (71, 6, 'toast_fine', 0, 0);


SET @col_exists = (SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'catalog_items' AND COLUMN_NAME = 'habbicon_id');
SET @ddl = IF(@col_exists = 0,
    'ALTER TABLE catalog_items ADD COLUMN habbicon_id INT NOT NULL DEFAULT 0', 'SELECT 1');
PREPARE habbicon_ddl FROM @ddl;
EXECUTE habbicon_ddl;
DEALLOCATE PREPARE habbicon_ddl;

INSERT INTO catalog_pages (parent_id, caption, page_link, page_layout, icon_image, min_rank, order_num, page_strings_1, page_strings_2)
SELECT -1, 'Habbicons', 'habbicons', 'default_3x3', 107, 1, 6, '', ''
FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM catalog_pages WHERE page_link = 'habbicons');
SET @habbicon_page = (SELECT MIN(id) FROM catalog_pages WHERE page_link = 'habbicons');

INSERT INTO catalog_items (item_id, page_id, catalog_name, cost_credits, cost_pixels, cost_diamonds, amount, offer_id, offer_active, habbicon_id)
SELECT '0', @habbicon_page, name, cost_credits,
       IF(points_type = 0, cost_points, 0), IF(points_type = 5, cost_points, 0), 1, -1, '1', id
FROM habbicons
WHERE (cost_credits > 0 OR cost_points > 0)
  AND NOT EXISTS (SELECT 1 FROM catalog_items existing_offer WHERE existing_offer.habbicon_id = habbicons.id);
