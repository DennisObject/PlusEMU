-- Apply explicitly before enabling configured Wired boxes. No existing Wired rows are rewritten.
CREATE TABLE IF NOT EXISTS `wired_item_configurations` (
    `item_id` int unsigned NOT NULL,
    `box_name` varchar(100) NOT NULL,
    `schema_version` int NOT NULL,
    `configuration` longtext NOT NULL,
    PRIMARY KEY (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
