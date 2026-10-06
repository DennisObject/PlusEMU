-- Convert legacy Unix seconds without depending on the MariaDB session time zone.
-- Zero and NULL represented unknown creation times and remain NULL.
ALTER TABLE chatlogs
    ADD COLUMN created_at_utc DATETIME(6) NULL AFTER `timestamp`;

UPDATE chatlogs
SET created_at_utc = CASE
    WHEN `timestamp` IS NULL OR `timestamp` <= 0 THEN NULL
    WHEN `timestamp` >= 253402300800 THEN NULL
    WHEN CAST(`timestamp` AS DECIMAL(30, 6)) > 253402300799.999999 THEN NULL
    ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL `timestamp` SECOND)
END;

ALTER TABLE chatlogs
    DROP COLUMN `timestamp`,
    CHANGE COLUMN created_at_utc `timestamp` DATETIME(6) NULL;
