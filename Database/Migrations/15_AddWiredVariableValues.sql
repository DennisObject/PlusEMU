-- Companion tables only: original wired_items and item data are not rewritten.
-- Definition lock rows serialize first writes as well as updates across room references.
CREATE TABLE IF NOT EXISTS `wired_variable_locks` (
    `definition_id` int unsigned NOT NULL,
    `retired` tinyint unsigned NOT NULL DEFAULT 0,
    PRIMARY KEY (`definition_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS `wired_variable_values` (
    `definition_id` int unsigned NOT NULL,
    `target_kind` tinyint unsigned NOT NULL,
    `holder_id` bigint NOT NULL,
    `value` int NOT NULL,
    `created_at_ms` bigint NOT NULL,
    `updated_at_ms` bigint NOT NULL,
    PRIMARY KEY (`definition_id`, `target_kind`, `holder_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
