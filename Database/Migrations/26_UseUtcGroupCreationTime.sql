-- Convert legacy Unix seconds without depending on the MariaDB session time zone.
-- Zero, negative and NULL represented unknown creation times and remain NULL.
-- Fractional seconds are retained to the microsecond and valid post-2038 values are preserved.
ALTER TABLE `groups`
    ADD COLUMN `created_at_utc` DATETIME(6) NULL AFTER `created`;

UPDATE `groups`
SET `created_at_utc` = CASE
    WHEN `created` IS NULL OR `created` <= 0 THEN NULL
    ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`created` * 1000000) MICROSECOND)
END;

ALTER TABLE `groups`
    DROP COLUMN `created`,
    CHANGE COLUMN `created_at_utc` `created` DATETIME(6) NULL;
