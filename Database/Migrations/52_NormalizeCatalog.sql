-- Replaces catalog_items and catalog_deals with offers, offer products and page placements, and gives the catalog
-- foreign keys. Apply while PlusEMU is stopped; SQL updates are not automatic. Written for MariaDB.
--
-- An offer's id is the id the client buys it with and the id furnidata names it by. A row's official offer_id is
-- kept; rows without one, and older rows reusing an official id for something else, get 1000000000 + their old row
-- id. Identical rows sharing an official id become one offer placed on each of their pages.
--
-- Rows the catalog never showed are not carried over: offers on missing or unreachable pages, offers with no amount,
-- offers whose furniture is missing, and the club rows of the vip_buy page (club offers live in catalog_club_offers).

-- Pages that cannot be reached from a root page.
CREATE TABLE `catalog_migration_reachable_pages` (
    `id` INT NOT NULL PRIMARY KEY
);

INSERT INTO `catalog_migration_reachable_pages` (`id`)
WITH RECURSIVE `reachable` AS (
    SELECT `id`
    FROM `catalog_pages`
    WHERE `parent_id` = -1
    UNION DISTINCT
    SELECT `page`.`id`
    FROM `catalog_pages` AS `page`
    INNER JOIN `reachable` AS `parent` ON `page`.`parent_id` = `parent`.`id`
)
SELECT `id` FROM `reachable`;

DELETE `page`
FROM `catalog_pages` AS `page`
LEFT JOIN `catalog_migration_reachable_pages` AS `reachable` ON `reachable`.`id` = `page`.`id`
WHERE `reachable`.`id` IS NULL;

DROP TABLE `catalog_migration_reachable_pages`;

-- Pages: NULL is the root, an empty link is no link, and the page strings move to their own tables.
ALTER TABLE `catalog_pages` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_uca1400_ai_ci;

ALTER TABLE `catalog_pages`
    DROP INDEX `id`,
    DROP INDEX `order_num`,
    MODIFY `parent_id` INT NULL DEFAULT NULL,
    CHANGE `order_num` `position` INT NOT NULL DEFAULT 0,
    CHANGE `page_link` `link` VARCHAR(128) NULL DEFAULT NULL,
    CHANGE `icon_image` `icon` INT NOT NULL DEFAULT 0,
    CHANGE `page_layout` `layout` VARCHAR(64) NOT NULL DEFAULT 'default_3x3',
    MODIFY `required_permission` VARCHAR(191) CHARACTER SET ascii COLLATE ascii_bin NULL DEFAULT NULL,
    MODIFY `visible` TINYINT(1) NOT NULL DEFAULT 1,
    MODIFY `enabled` TINYINT(1) NOT NULL DEFAULT 1;

UPDATE `catalog_pages` SET `parent_id` = NULL WHERE `parent_id` = -1;
UPDATE `catalog_pages` SET `link` = NULL WHERE `link` = '';
UPDATE `catalog_pages` SET `required_permission` = NULL WHERE `required_permission` = '';

ALTER TABLE `catalog_pages`
    ADD UNIQUE KEY `link` (`link`),
    ADD KEY `tree` (`parent_id`, `position`, `id`),
    ADD CONSTRAINT `fk_catalog_pages_parent` FOREIGN KEY (`parent_id`) REFERENCES `catalog_pages` (`id`),
    ADD CONSTRAINT `fk_catalog_pages_permission` FOREIGN KEY (`required_permission`) REFERENCES `acl_permissions` (`key`);

-- page_strings_1 held the page images and page_strings_2 its texts, '|'-separated by slot.
CREATE TABLE `catalog_page_images` (
    `page_id` INT NOT NULL,
    `slot` TINYINT UNSIGNED NOT NULL,
    `image` VARCHAR(255) NOT NULL,
    PRIMARY KEY (`page_id`, `slot`),
    CONSTRAINT `fk_catalog_page_images_page` FOREIGN KEY (`page_id`) REFERENCES `catalog_pages` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;

CREATE TABLE `catalog_page_texts` (
    `page_id` INT NOT NULL,
    `slot` TINYINT UNSIGNED NOT NULL,
    `text` TEXT NOT NULL,
    PRIMARY KEY (`page_id`, `slot`),
    CONSTRAINT `fk_catalog_page_texts_page` FOREIGN KEY (`page_id`) REFERENCES `catalog_pages` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;

INSERT INTO `catalog_page_images` (`page_id`, `slot`, `image`)
WITH RECURSIVE `part` AS (
    SELECT `id`, 0 AS `slot`, SUBSTRING_INDEX(`page_strings_1`, '|', 1) AS `value`,
        IF(LOCATE('|', `page_strings_1`) > 0, SUBSTRING(`page_strings_1`, LOCATE('|', `page_strings_1`) + 1), NULL) AS `rest`
    FROM `catalog_pages`
    WHERE TRIM(`page_strings_1`) <> ''
    UNION ALL
    SELECT `id`, `slot` + 1, SUBSTRING_INDEX(`rest`, '|', 1), IF(LOCATE('|', `rest`) > 0, SUBSTRING(`rest`, LOCATE('|', `rest`) + 1), NULL)
    FROM `part`
    WHERE `rest` IS NOT NULL
)
SELECT `id`, `slot`, `value` FROM `part`;

INSERT INTO `catalog_page_texts` (`page_id`, `slot`, `text`)
WITH RECURSIVE `part` AS (
    SELECT `id`, 0 AS `slot`, SUBSTRING_INDEX(`page_strings_2`, '|', 1) AS `value`,
        IF(LOCATE('|', `page_strings_2`) > 0, SUBSTRING(`page_strings_2`, LOCATE('|', `page_strings_2`) + 1), NULL) AS `rest`
    FROM `catalog_pages`
    WHERE TRIM(`page_strings_2`) <> ''
    UNION ALL
    SELECT `id`, `slot` + 1, SUBSTRING_INDEX(`rest`, '|', 1), IF(LOCATE('|', `rest`) > 0, SUBSTRING(`rest`, LOCATE('|', `rest`) + 1), NULL)
    FROM `part`
    WHERE `rest` IS NOT NULL
)
SELECT `id`, `slot`, `value` FROM `part`;

ALTER TABLE `catalog_pages`
    DROP COLUMN `page_strings_1`,
    DROP COLUMN `page_strings_2`;

-- Offers, what each one sells, and the pages it is shown on.
CREATE TABLE `catalog_offers` (
    `id` INT NOT NULL AUTO_INCREMENT,
    `localization_key` VARCHAR(100) NOT NULL,
    `cost_credits` INT UNSIGNED NOT NULL DEFAULT 0,
    `cost_points` INT UNSIGNED NOT NULL DEFAULT 0,
    -- 0 duckets, 5 diamonds.
    `points_type` INT UNSIGNED NOT NULL DEFAULT 0,
    `club_level` TINYINT UNSIGNED NOT NULL DEFAULT 0,
    -- The client may buy several at once.
    `bulk_purchase` TINYINT(1) NOT NULL DEFAULT 1,
    `enabled` TINYINT(1) NOT NULL DEFAULT 1,
    `preview_image` VARCHAR(255) NOT NULL DEFAULT '',
    PRIMARY KEY (`id`),
    CONSTRAINT `ck_catalog_offers_club_level` CHECK (`club_level` <= 2)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;

CREATE TABLE `catalog_offer_products` (
    `offer_id` INT NOT NULL,
    `position` TINYINT UNSIGNED NOT NULL,
    `product_type` ENUM('furni', 'effect', 'badge', 'bot', 'pet', 'habbicon') NOT NULL,
    `furniture_id` INT UNSIGNED NULL DEFAULT NULL,
    `effect_id` INT NULL DEFAULT NULL,
    -- Matches badge_definitions.code.
    `badge_code` VARCHAR(35) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NULL DEFAULT NULL,
    `bot_preset_id` INT NULL DEFAULT NULL,
    `pet_type` INT NULL DEFAULT NULL,
    `habbicon_id` INT NULL DEFAULT NULL,
    `amount` INT UNSIGNED NOT NULL DEFAULT 1,
    `extra_param` VARCHAR(1024) NOT NULL DEFAULT '',
    PRIMARY KEY (`offer_id`, `position`),
    KEY `furniture_id` (`furniture_id`),
    CONSTRAINT `fk_catalog_offer_products_offer` FOREIGN KEY (`offer_id`) REFERENCES `catalog_offers` (`id`) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT `fk_catalog_offer_products_furniture` FOREIGN KEY (`furniture_id`) REFERENCES `furniture` (`id`),
    CONSTRAINT `fk_catalog_offer_products_badge` FOREIGN KEY (`badge_code`) REFERENCES `badge_definitions` (`code`),
    CONSTRAINT `fk_catalog_offer_products_bot` FOREIGN KEY (`bot_preset_id`) REFERENCES `catalog_bot_presets` (`id`),
    CONSTRAINT `fk_catalog_offer_products_habbicon` FOREIGN KEY (`habbicon_id`) REFERENCES `habbicons` (`id`),
    CONSTRAINT `ck_catalog_offer_products_amount` CHECK (`amount` BETWEEN 1 AND 1000),
    CONSTRAINT `ck_catalog_offer_products_target` CHECK (
        (`product_type` = 'furni') = (`furniture_id` IS NOT NULL) AND
        (`product_type` = 'effect') = (`effect_id` IS NOT NULL) AND
        (`product_type` = 'badge') = (`badge_code` IS NOT NULL) AND
        (`product_type` = 'bot') = (`bot_preset_id` IS NOT NULL) AND
        (`product_type` = 'pet') = (`pet_type` IS NOT NULL) AND
        (`product_type` = 'habbicon') = (`habbicon_id` IS NOT NULL))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;

-- Limited editions; sold is the last serial handed out.
CREATE TABLE `catalog_offer_limited` (
    `offer_id` INT NOT NULL,
    `stack` INT UNSIGNED NOT NULL,
    `sold` INT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (`offer_id`),
    CONSTRAINT `fk_catalog_offer_limited_offer` FOREIGN KEY (`offer_id`) REFERENCES `catalog_offers` (`id`) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT `ck_catalog_offer_limited_stock` CHECK (`stack` > 0 AND `sold` <= `stack`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;

CREATE TABLE `catalog_page_offers` (
    `page_id` INT NOT NULL,
    `offer_id` INT NOT NULL,
    `position` INT NOT NULL DEFAULT 0,
    PRIMARY KEY (`page_id`, `offer_id`),
    KEY `offer_id` (`offer_id`),
    KEY `page_order` (`page_id`, `position`),
    CONSTRAINT `fk_catalog_page_offers_page` FOREIGN KEY (`page_id`) REFERENCES `catalog_pages` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_catalog_page_offers_offer` FOREIGN KEY (`offer_id`) REFERENCES `catalog_offers` (`id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;

-- Every carried-over row, its group of identical rows (same official offer id and content) and its offer id.
CREATE TABLE `catalog_migration_rows` (
    `row_id` INT NOT NULL PRIMARY KEY,
    `page_id` INT NOT NULL,
    `page_position` INT NOT NULL,
    `kind` VARCHAR(8) NOT NULL,
    `furniture_id` INT UNSIGNED NULL,
    `deal_id` INT NULL,
    `effect_id` INT NULL,
    `badge_code` VARCHAR(64) NULL,
    `bot_preset_id` INT NULL,
    `pet_type` INT NULL,
    `habbicon_id` INT NULL,
    `catalog_name` VARCHAR(100) NOT NULL,
    `cost_credits` INT NOT NULL,
    `cost_pixels` INT NOT NULL,
    `cost_diamonds` INT NOT NULL,
    `amount` INT NOT NULL,
    `limited_sells` INT NOT NULL,
    `limited_stack` INT NOT NULL,
    `offer_active` TINYINT(1) NOT NULL,
    `extradata` VARCHAR(1024) NOT NULL,
    `badge` VARCHAR(64) NOT NULL,
    `club_level` TINYINT UNSIGNED NOT NULL,
    `preview_image` VARCHAR(255) NOT NULL,
    `official_id` INT NOT NULL,
    `group_row` INT NOT NULL,
    `offer_id` INT NULL,
    KEY `official` (`official_id`, `group_row`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;

INSERT INTO `catalog_migration_rows` (`row_id`, `page_id`, `page_position`, `kind`, `furniture_id`, `deal_id`, `effect_id`, `badge_code`,
    `bot_preset_id`, `pet_type`, `habbicon_id`, `catalog_name`, `cost_credits`, `cost_pixels`, `cost_diamonds`, `amount`, `limited_sells`,
    `limited_stack`, `offer_active`, `extradata`, `badge`, `club_level`, `preview_image`, `official_id`, `group_row`)
SELECT `row_id`, `page_id`, `page_position`, `kind`, `furniture_id`, `deal_id`, `effect_id`, `badge_code`, `bot_preset_id`, `pet_type`,
    `habbicon_id`, `catalog_name`, `cost_credits`, `cost_pixels`, `cost_diamonds`, `amount`, `limited_sells`, `limited_stack`, `offer_active`,
    `extradata`, `badge`, `club_level`, `preview_image`, `official_id`,
    -- Limited editions and rows without an official id are never merged.
    IF(`official_id` > 0 AND `limited_stack` <= 0, MIN(`row_id`) OVER (PARTITION BY `official_id`, `content`), `row_id`)
FROM (
    SELECT `row_id`, `page_id`, ROW_NUMBER() OVER (PARTITION BY `page_id` ORDER BY `order_num`, `row_id`) - 1 AS `page_position`, `kind`,
        IF(`kind` = 'furni', `furni_id`, NULL) AS `furniture_id`,
        IF(`kind` = 'deal', `behaviour_data`, NULL) AS `deal_id`,
        IF(`kind` = 'effect', `sprite_id`, NULL) AS `effect_id`,
        IF(`kind` = 'badge', `item_name`, NULL) AS `badge_code`,
        IF(`kind` = 'bot', `furni_id`, NULL) AS `bot_preset_id`,
        IF(`kind` = 'pet', `behaviour_data`, NULL) AS `pet_type`,
        IF(`kind` = 'habbicon', `habbicon_id`, NULL) AS `habbicon_id`,
        `catalog_name`, `cost_credits`, `cost_pixels`, `cost_diamonds`, `amount`, `limited_sells`, `limited_stack`, `offer_active`,
        -- Wallpaper, floor and landscape offers named their pattern as the third part of the catalog name.
        IF(LOWER(`interaction_type`) IN ('wallpaper', 'floor', 'landscape'),
            IF(LENGTH(`catalog_name`) - LENGTH(REPLACE(`catalog_name`, '_', '')) >= 2, SUBSTRING_INDEX(SUBSTRING_INDEX(`catalog_name`, '_', 3), '_', -1), ''), `extradata`) AS `extradata`,
        `badge`, `club_level`, `preview_image`, GREATEST(`offer_id`, 0) AS `official_id`,
        SHA1(CONCAT_WS(0x1F, `kind`, IFNULL(`furni_id`, 0), `habbicon_id`, `catalog_name`, `cost_credits`, `cost_pixels`, `cost_diamonds`,
            `amount`, `offer_active`, `extradata`, `badge`, `club_level`, `preview_image`)) AS `content`
    FROM (
        SELECT `item`.`id` AS `row_id`, `item`.`page_id`, `item`.`order_num`, `item`.`catalog_name`, `item`.`cost_credits`, `item`.`cost_pixels`,
            `item`.`cost_diamonds`, `item`.`amount`, `item`.`limited_sells`, `item`.`limited_stack`, `item`.`offer_active`, `item`.`extradata`,
            `item`.`badge`, `item`.`club_level`, `item`.`preview_image`, `item`.`offer_id`, `item`.`habbicon_id`,
            `furni`.`id` AS `furni_id`, `furni`.`sprite_id`, `furni`.`item_name`, `furni`.`behaviour_data`, `furni`.`interaction_type`,
            CASE
                WHEN `item`.`habbicon_id` > 0 THEN 'habbicon'
                WHEN LOWER(`furni`.`interaction_type`) IN ('deal', 'roomdeal') THEN 'deal'
                WHEN `furni`.`type` IN ('s', 'i') THEN 'furni'
                WHEN `furni`.`type` = 'e' THEN 'effect'
                WHEN `furni`.`type` = 'b' THEN 'badge'
                WHEN `furni`.`type` = 'r' THEN 'bot'
                WHEN `furni`.`type` = 'p' THEN 'pet'
            END AS `kind`
        FROM `catalog_items` AS `item`
        INNER JOIN `catalog_pages` AS `page` ON `page`.`id` = `item`.`page_id`
        LEFT JOIN `furniture` AS `furni` ON `item`.`habbicon_id` <= 0
            AND `furni`.`id` = CAST(REGEXP_SUBSTR(`item`.`item_id`, '[0-9]+') AS UNSIGNED)
        WHERE `item`.`amount` > 0
    ) AS `typed`
    WHERE `kind` IS NOT NULL
) AS `rows`;

-- For each official id, the group holding its newest row keeps it: later imports (the wired catalogue) were written
-- from furnidata, while the original rows reused some official ids for other furniture.
UPDATE `catalog_migration_rows` AS `row`
LEFT JOIN (
    SELECT `newest`.`official_id`, `owner`.`group_row` AS `winner`
    FROM (
        SELECT `official_id`, MAX(`row_id`) AS `row_id`
        FROM `catalog_migration_rows`
        WHERE `official_id` > 0
        GROUP BY `official_id`
    ) AS `newest`
    INNER JOIN `catalog_migration_rows` AS `owner` ON `owner`.`row_id` = `newest`.`row_id`
) AS `official` ON `official`.`official_id` = `row`.`official_id`
SET `row`.`offer_id` = IF(`row`.`group_row` = `official`.`winner`, `row`.`official_id`, 1000000000 + `row`.`group_row`);

INSERT INTO `catalog_offers` (`id`, `localization_key`, `cost_credits`, `cost_points`, `points_type`, `club_level`, `bulk_purchase`, `enabled`, `preview_image`)
SELECT `offer_id`, `catalog_name`, `cost_credits`, IF(`cost_diamonds` > 0, `cost_diamonds`, `cost_pixels`), IF(`cost_diamonds` > 0, 5, 0), `club_level`,
    -- Habbicon rows used offer_active for whether the offer is enabled.
    IF(`kind` = 'habbicon', 0, `offer_active`), IF(`kind` = 'habbicon', `offer_active`, 1), `preview_image`
FROM `catalog_migration_rows`
WHERE `row_id` = `group_row`;

ALTER TABLE `catalog_offers` AUTO_INCREMENT = 1000000000;

INSERT INTO `catalog_page_offers` (`page_id`, `offer_id`, `position`)
SELECT `page_id`, `offer_id`, MIN(`page_position`)
FROM `catalog_migration_rows`
GROUP BY `page_id`, `offer_id`;

-- Badges an offer gives must be defined.
INSERT IGNORE INTO `badge_definitions` (`code`)
SELECT DISTINCT `code`
FROM (
    SELECT `badge_code` AS `code` FROM `catalog_migration_rows` WHERE `row_id` = `group_row` AND `kind` = 'badge'
    UNION ALL
    SELECT `badge` FROM `catalog_migration_rows` WHERE `row_id` = `group_row` AND `kind` NOT IN ('badge', 'habbicon') AND `badge` <> ''
) AS `badges`;

-- An attached badge comes first, as the client has always received it.
INSERT INTO `catalog_offer_products` (`offer_id`, `position`, `product_type`, `badge_code`)
SELECT `offer_id`, 0, 'badge', `badge`
FROM `catalog_migration_rows`
WHERE `row_id` = `group_row` AND `kind` NOT IN ('badge', 'habbicon') AND `badge` <> '';

INSERT INTO `catalog_offer_products` (`offer_id`, `position`, `product_type`, `furniture_id`, `effect_id`, `badge_code`, `bot_preset_id`,
    `pet_type`, `habbicon_id`, `amount`, `extra_param`)
SELECT `offer_id`, IF(`kind` NOT IN ('badge', 'habbicon') AND `badge` <> '', 1, 0), `kind`, `furniture_id`, `effect_id`, `badge_code`,
    `bot_preset_id`, `pet_type`, `habbicon_id`, IF(`kind` IN ('badge', 'habbicon'), 1, `amount`), IF(`kind` = 'furni', `extradata`, '')
FROM `catalog_migration_rows`
WHERE `row_id` = `group_row` AND `kind` <> 'deal';

-- A deal's furniture, from its 'furnitureId*amount;...' list.
INSERT INTO `catalog_offer_products` (`offer_id`, `position`, `product_type`, `furniture_id`, `amount`)
WITH RECURSIVE `part` AS (
    SELECT `row`.`offer_id`, IF(`row`.`badge` <> '', 1, 0) AS `slot`, SUBSTRING_INDEX(`deal`.`items`, ';', 1) AS `value`,
        IF(LOCATE(';', `deal`.`items`) > 0, SUBSTRING(`deal`.`items`, LOCATE(';', `deal`.`items`) + 1), NULL) AS `rest`
    FROM `catalog_migration_rows` AS `row`
    INNER JOIN `catalog_deals` AS `deal` ON `deal`.`id` = `row`.`deal_id`
    WHERE `row`.`row_id` = `row`.`group_row` AND `row`.`kind` = 'deal' AND TRIM(`deal`.`items`) <> ''
    UNION ALL
    SELECT `offer_id`, `slot` + 1, SUBSTRING_INDEX(`rest`, ';', 1), IF(LOCATE(';', `rest`) > 0, SUBSTRING(`rest`, LOCATE(';', `rest`) + 1), NULL)
    FROM `part`
    WHERE `rest` IS NOT NULL
)
SELECT `part`.`offer_id`, `part`.`slot`, 'furni', `furni`.`id`, GREATEST(CAST(REGEXP_SUBSTR(SUBSTRING_INDEX(`part`.`value`, '*', -1), '[0-9]+') AS UNSIGNED), 1)
FROM `part`
INNER JOIN `furniture` AS `furni` ON `furni`.`id` = CAST(REGEXP_SUBSTR(SUBSTRING_INDEX(`part`.`value`, '*', 1), '[0-9]+') AS UNSIGNED)
WHERE `part`.`value` LIKE '%*%';

INSERT INTO `catalog_offer_limited` (`offer_id`, `stack`, `sold`)
SELECT `offer_id`, `limited_stack`, LEAST(GREATEST(`limited_sells`, 0), `limited_stack`)
FROM `catalog_migration_rows`
WHERE `row_id` = `group_row` AND `limited_stack` > 0;

-- Club gifts name offers. A claim is history and keeps the offer it was.
CREATE TABLE `catalog_migration_club_gift_offers` (
    `offer_id` INT NOT NULL,
    `days_required` INT NOT NULL DEFAULT 0,
    `enabled` TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`offer_id`),
    CONSTRAINT `fk_club_gift_offers_offer` FOREIGN KEY (`offer_id`) REFERENCES `catalog_offers` (`id`) ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_uca1400_ai_ci;

INSERT IGNORE INTO `catalog_migration_club_gift_offers` (`offer_id`, `days_required`, `enabled`)
SELECT `row`.`offer_id`, `gift`.`days_required`, `gift`.`enabled`
FROM `club_gift_offers` AS `gift`
INNER JOIN `catalog_migration_rows` AS `row` ON `row`.`row_id` = `gift`.`catalog_item_id`
ORDER BY `gift`.`catalog_item_id`;

DROP TABLE `club_gift_offers`;
RENAME TABLE `catalog_migration_club_gift_offers` TO `club_gift_offers`;

UPDATE `club_gift_claims` AS `claim`
INNER JOIN `catalog_migration_rows` AS `row` ON `row`.`row_id` = `claim`.`catalog_item_id`
SET `claim`.`catalog_item_id` = `row`.`offer_id`;

ALTER TABLE `club_gift_claims` CHANGE `catalog_item_id` `offer_id` INT NOT NULL;

-- The editor's history names offers by their new ids.
UPDATE `catalog_admin_log` AS `log`
INNER JOIN `catalog_migration_rows` AS `row` ON `row`.`row_id` = `log`.`entity_id`
SET `log`.`entity_id` = `row`.`offer_id`
WHERE `log`.`entity_type` = 'OFFER';

DROP TABLE `catalog_migration_rows`;
DROP TABLE `catalog_items`;
DROP TABLE `catalog_deals`;
