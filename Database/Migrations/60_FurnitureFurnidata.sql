-- Furniture definitions own their furnidata: the emulator generates FurnitureData.json from these columns plus the
-- catalog's offers. Apply while PlusEMU is stopped; SQL updates are not automatic. Written for MariaDB.
--
-- A row with has_furnidata is the furnidata entry of its kind (s = roomitemtypes, i = wallitemtypes): its sprite_id
-- is the entry id and its item_name the classname. Rows that share a classname with that row (Plus duplicates of a
-- furni with another interaction) use its entry and keep has_furnidata off. Sprite ids are unique per kind and
-- classnames unique among furnidata rows.
--
-- Run it with the hotel's current FurnitureData.json in @furnidata to fill the columns from it:
--   { printf "SET @furnidata = FROM_BASE64('%s');\n" "$(base64 -w0 FurnitureData.json)"; cat 60_FurnitureFurnidata.sql; } | mariadb plus
-- Each entry goes to the row of its kind with exactly its classname, preferring the row whose sprite id is the entry id,
-- then the oldest row; that row's sprite id becomes the entry id. Entries no row has get a new row. Without
-- @furnidata only the columns are added, and the emulator serves no furnidata until rows have it.

ALTER TABLE `furniture`
    ADD COLUMN IF NOT EXISTS `has_furnidata` BOOL NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS `revision` INT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `category` VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS `default_dir` INT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS `xdim` INT NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS `ydim` INT NOT NULL DEFAULT 1,
    -- Comma-separated colours; NULL leaves partcolors out, '' is an empty list.
    ADD COLUMN IF NOT EXISTS `part_colors` VARCHAR(1024) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS `name` VARCHAR(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS `description` VARCHAR(1024) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS `ad_url` VARCHAR(512) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS `excluded_dynamic` BOOL NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS `custom_params` VARCHAR(1024) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS `special_type` INT NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS `can_stand_on` BOOL NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS `can_sit_on` BOOL NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS `can_lay_on` BOOL NOT NULL DEFAULT FALSE,
    -- NULL leaves the field out of the entry.
    ADD COLUMN IF NOT EXISTS `can_put_stuff_on` BOOL NULL DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS `height` DOUBLE NULL DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS `furni_line` VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS `environment` VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS `rare` BOOL NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS `tradeable` BOOL NULL DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS `recyclable` BOOL NULL DEFAULT NULL,
    ADD COLUMN IF NOT EXISTS `furnidata_sprite_id` INT AS (IF(`has_furnidata`, `sprite_id`, NULL)) PERSISTENT,
    ADD COLUMN IF NOT EXISTS `furnidata_classname` VARCHAR(70) AS (IF(`has_furnidata`, `item_name`, NULL)) PERSISTENT,
    ADD UNIQUE KEY IF NOT EXISTS `furnidata_sprite_id` (`type`, `furnidata_sprite_id`),
    ADD UNIQUE KEY IF NOT EXISTS `furnidata_classname` (`furnidata_classname`),
    ADD CONSTRAINT IF NOT EXISTS `furnidata_kind` CHECK (NOT `has_furnidata` OR `type` IN ('s', 'i'));

-- Entries of @furnidata, and the row each one becomes.
CREATE TABLE `furnidata_migration_entries` (
    `kind` CHAR(1) CHARACTER SET latin1 COLLATE latin1_nopad_bin NOT NULL,
    `id` INT NOT NULL,
    `classname` VARCHAR(70) CHARACTER SET latin1 COLLATE latin1_nopad_bin NOT NULL,
    `revision` INT NOT NULL,
    `category` VARCHAR(64) CHARACTER SET utf8mb4 NULL,
    `default_dir` INT NOT NULL,
    `xdim` INT NOT NULL,
    `ydim` INT NOT NULL,
    `part_colors` VARCHAR(1024) CHARACTER SET utf8mb4 NULL,
    `name` VARCHAR(255) CHARACTER SET utf8mb4 NULL,
    `description` VARCHAR(1024) CHARACTER SET utf8mb4 NULL,
    `ad_url` VARCHAR(512) CHARACTER SET utf8mb4 NULL,
    `excluded_dynamic` BOOL NOT NULL,
    `custom_params` VARCHAR(1024) CHARACTER SET utf8mb4 NULL,
    `special_type` INT NOT NULL,
    `can_stand_on` BOOL NOT NULL,
    `can_sit_on` BOOL NOT NULL,
    `can_lay_on` BOOL NOT NULL,
    `can_put_stuff_on` BOOL NULL,
    `height` DOUBLE NULL,
    `furni_line` VARCHAR(64) CHARACTER SET utf8mb4 NULL,
    `environment` VARCHAR(64) CHARACTER SET utf8mb4 NULL,
    `rare` BOOL NOT NULL,
    `tradeable` BOOL NULL,
    `recyclable` BOOL NULL,
    `furniture_id` INT UNSIGNED NULL,
    PRIMARY KEY (`kind`, `id`),
    UNIQUE KEY `classname` (`kind`, `classname`)
);

SET @furnidata_json = CONVERT(@furnidata USING utf8mb4);

INSERT INTO `furnidata_migration_entries` (`kind`, `id`, `classname`, `revision`, `category`, `default_dir`, `xdim`, `ydim`, `part_colors`, `name`,
    `description`, `ad_url`, `excluded_dynamic`, `custom_params`, `special_type`, `can_stand_on`, `can_sit_on`, `can_lay_on`,
    `can_put_stuff_on`, `height`, `furni_line`, `environment`, `rare`, `tradeable`, `recyclable`)
SELECT `section`.`kind`, `entry`.`id`, `entry`.`classname`, COALESCE(`entry`.`revision`, 0), `entry`.`category`,
    COALESCE(`entry`.`default_dir`, 0), COALESCE(`entry`.`xdim`, 1), COALESCE(`entry`.`ydim`, 1),
    -- partcolors.color as a comma-separated list; the list has no element that contains a comma.
    IF(`entry`.`has_part_colors`, COALESCE((
        SELECT GROUP_CONCAT(`colour`.`value` ORDER BY `colour`.`position` SEPARATOR ',')
        FROM JSON_TABLE(`entry`.`part_colors`, '$[*]' COLUMNS (`position` FOR ORDINALITY, `value` VARCHAR(64) PATH '$')) AS `colour`
    ), ''), NULL),
    `entry`.`name`, `entry`.`description`, `entry`.`ad_url`, COALESCE(`entry`.`excluded_dynamic`, FALSE), `entry`.`custom_params`,
    COALESCE(`entry`.`special_type`, 1), COALESCE(`entry`.`can_stand_on`, FALSE), COALESCE(`entry`.`can_sit_on`, FALSE),
    COALESCE(`entry`.`can_lay_on`, FALSE), `entry`.`can_put_stuff_on`, `entry`.`height`, `entry`.`furni_line`, `entry`.`environment`,
    COALESCE(`entry`.`rare`, FALSE), `entry`.`tradeable`, `entry`.`recyclable`
FROM (SELECT 's' AS `kind`, '$.roomitemtypes.furnitype' AS `path` UNION ALL SELECT 'i', '$.wallitemtypes.furnitype') AS `section`
CROSS JOIN JSON_TABLE(JSON_EXTRACT(@furnidata_json, `section`.`path`), '$[*]' COLUMNS (
    `id` INT PATH '$.id',
    `classname` VARCHAR(70) PATH '$.classname',
    `revision` INT PATH '$.revision',
    `category` VARCHAR(64) PATH '$.category',
    `default_dir` INT PATH '$.defaultdir',
    `xdim` INT PATH '$.xdim',
    `ydim` INT PATH '$.ydim',
    `has_part_colors` INT EXISTS PATH '$.partcolors',
    `part_colors` JSON PATH '$.partcolors.color',
    `name` VARCHAR(255) PATH '$.name',
    `description` VARCHAR(1024) PATH '$.description',
    `ad_url` VARCHAR(512) PATH '$.adurl',
    `excluded_dynamic` BOOL PATH '$.excludeddynamic',
    `custom_params` VARCHAR(1024) PATH '$.customparams',
    `special_type` INT PATH '$.specialtype',
    `can_stand_on` BOOL PATH '$.canstandon',
    `can_sit_on` BOOL PATH '$.cansiton',
    `can_lay_on` BOOL PATH '$.canlayon',
    `can_put_stuff_on` BOOL PATH '$.canputstuffon',
    `height` DOUBLE PATH '$.height',
    `furni_line` VARCHAR(64) PATH '$.furniline',
    `environment` VARCHAR(64) PATH '$.environment',
    `rare` BOOL PATH '$.rare',
    `tradeable` BOOL PATH '$.tradeable',
    `recyclable` BOOL PATH '$.recyclable'
)) AS `entry`
WHERE @furnidata_json IS NOT NULL;

-- The row that becomes each entry: same kind, exactly the classname, its sprite id first, then the oldest.
UPDATE `furnidata_migration_entries` AS `entry`
INNER JOIN (
    SELECT `candidate`.`kind`, `candidate`.`id`,
        COALESCE(MIN(IF(`furniture`.`sprite_id` = `candidate`.`id`, `furniture`.`id`, NULL)), MIN(`furniture`.`id`)) AS `furniture_id`
    FROM `furniture`
    INNER JOIN `furnidata_migration_entries` AS `candidate` ON `candidate`.`kind` = `furniture`.`type`
        AND `candidate`.`classname` = `furniture`.`item_name` COLLATE latin1_nopad_bin
    GROUP BY `candidate`.`kind`, `candidate`.`id`
) AS `owner` ON `owner`.`kind` = `entry`.`kind` AND `owner`.`id` = `entry`.`id`
SET `entry`.`furniture_id` = `owner`.`furniture_id`;

-- Entries no row has get one.
INSERT INTO `furniture` (`item_name`, `public_name`, `type`, `width`, `length`, `can_sit`, `is_walkable`, `sprite_id`)
-- public_name is latin1; characters outside it become '?'.
SELECT `classname`, CONVERT(REGEXP_REPLACE(LEFT(COALESCE(`name`, ''), 56), '[^\\x{0}-\\x{FF}]', '?') USING latin1), `kind`, `xdim`, `ydim`, `can_sit_on`, `can_stand_on`, `id`
FROM `furnidata_migration_entries`
WHERE `furniture_id` IS NULL
ORDER BY `kind` DESC, `id`;

UPDATE `furnidata_migration_entries` AS `entry`
INNER JOIN `furniture` ON `furniture`.`type` = `entry`.`kind` AND `furniture`.`item_name` COLLATE latin1_nopad_bin = `entry`.`classname`
    AND `furniture`.`sprite_id` = `entry`.`id`
SET `entry`.`furniture_id` = `furniture`.`id`
WHERE `entry`.`furniture_id` IS NULL;

UPDATE `furniture`
INNER JOIN `furnidata_migration_entries` AS `entry` ON `entry`.`furniture_id` = `furniture`.`id`
SET `furniture`.`has_furnidata` = TRUE, `furniture`.`sprite_id` = `entry`.`id`, `furniture`.`revision` = `entry`.`revision`,
    `furniture`.`category` = `entry`.`category`, `furniture`.`default_dir` = `entry`.`default_dir`, `furniture`.`xdim` = `entry`.`xdim`,
    `furniture`.`ydim` = `entry`.`ydim`, `furniture`.`part_colors` = `entry`.`part_colors`, `furniture`.`name` = `entry`.`name`,
    `furniture`.`description` = `entry`.`description`, `furniture`.`ad_url` = `entry`.`ad_url`,
    `furniture`.`excluded_dynamic` = `entry`.`excluded_dynamic`, `furniture`.`custom_params` = `entry`.`custom_params`,
    `furniture`.`special_type` = `entry`.`special_type`, `furniture`.`can_stand_on` = `entry`.`can_stand_on`,
    `furniture`.`can_sit_on` = `entry`.`can_sit_on`, `furniture`.`can_lay_on` = `entry`.`can_lay_on`,
    `furniture`.`can_put_stuff_on` = `entry`.`can_put_stuff_on`, `furniture`.`height` = `entry`.`height`,
    `furniture`.`furni_line` = `entry`.`furni_line`, `furniture`.`environment` = `entry`.`environment`, `furniture`.`rare` = `entry`.`rare`,
    `furniture`.`tradeable` = `entry`.`tradeable`, `furniture`.`recyclable` = `entry`.`recyclable`;

DROP TABLE `furnidata_migration_entries`;
SET @furnidata_json = NULL;
