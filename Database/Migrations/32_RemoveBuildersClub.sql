-- Remove Builders Club catalog and reward configuration without touching furniture definitions
-- or items already owned by users.
CREATE TEMPORARY TABLE `removed_builders_club_pages` (
    `id` INT NOT NULL PRIMARY KEY
);

INSERT INTO `removed_builders_club_pages` (`id`)
WITH RECURSIVE `builders_club_pages` AS (
    SELECT `id`
    FROM `catalog_pages`
    WHERE `catalog_mode` = 'BUILDERS_CLUB'
        OR (`id` = 9027 AND `caption` = 'Builders Club')
    UNION DISTINCT
    SELECT `page`.`id`
    FROM `catalog_pages` AS `page`
    INNER JOIN `builders_club_pages` AS `parent` ON `page`.`parent_id` = `parent`.`id`
)
SELECT `id` FROM `builders_club_pages`;

DELETE `item`
FROM `catalog_items` AS `item`
INNER JOIN `removed_builders_club_pages` AS `page` ON `page`.`id` = `item`.`page_id`;

DELETE `page`
FROM `catalog_pages` AS `page`
INNER JOIN `removed_builders_club_pages` AS `removed` ON `removed`.`id` = `page`.`id`;

DROP TEMPORARY TABLE `removed_builders_club_pages`;

CREATE TEMPORARY TABLE `removed_builders_club_tasks` (
    `track_id` VARCHAR(64) NOT NULL,
    `id` VARCHAR(64) NOT NULL,
    PRIMARY KEY (`track_id`, `id`)
);

INSERT INTO `removed_builders_club_tasks` (`track_id`, `id`)
SELECT `track_id`, `id`
FROM `reward_track_tasks`
WHERE `action_type` = 'place_builders_club_furni' OR `id` = 'place_builders_club_furni';

DELETE `progress`
FROM `users_reward_track_tasks` AS `progress`
INNER JOIN `removed_builders_club_tasks` AS `task`
    ON BINARY `task`.`track_id` = BINARY `progress`.`track_id` AND BINARY `task`.`id` = BINARY `progress`.`task_id`;

DELETE `level`
FROM `reward_track_task_levels` AS `level`
INNER JOIN `removed_builders_club_tasks` AS `task`
    ON BINARY `task`.`track_id` = BINARY `level`.`track_id` AND BINARY `task`.`id` = BINARY `level`.`task_id`;

DELETE `task`
FROM `reward_track_tasks` AS `task`
INNER JOIN `removed_builders_club_tasks` AS `removed`
    ON BINARY `removed`.`track_id` = BINARY `task`.`track_id` AND BINARY `removed`.`id` = BINARY `task`.`id`;

DROP TEMPORARY TABLE `removed_builders_club_tasks`;

DELETE FROM `catalog_admin_log` WHERE `catalog_type` = 'BUILDER';

ALTER TABLE `catalog_pages` DROP COLUMN `catalog_mode`;
ALTER TABLE `catalog_admin_log` MODIFY COLUMN `catalog_type` ENUM('NORMAL') NOT NULL;
