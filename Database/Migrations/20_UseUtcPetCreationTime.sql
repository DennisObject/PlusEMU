-- Convert legacy Unix seconds without depending on the MariaDB session time zone.
-- Zero and NULL represented unknown creation times and remain NULL.
ALTER TABLE bots_petdata
    ADD COLUMN created_at_utc DATETIME(6) NULL AFTER createstamp;

UPDATE bots_petdata
SET created_at_utc = CASE
    WHEN createstamp IS NULL OR createstamp <= 0 THEN NULL
    ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL createstamp SECOND)
END;

ALTER TABLE bots_petdata
    DROP COLUMN createstamp,
    CHANGE COLUMN created_at_utc createstamp DATETIME(6) NULL;
