-- One locked row serializes both per-player claims and the box's global prize limit.
CREATE TABLE IF NOT EXISTS `wired_reward_state` (
    `item_id` INT UNSIGNED NOT NULL,
    `claims` LONGTEXT NOT NULL,
    PRIMARY KEY (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
