-- Convert legacy Unix seconds without depending on the MariaDB session time zone.
-- Zero and NULL represented unknown visit times and remain NULL.
ALTER TABLE user_roomvisits
    ADD COLUMN entered_at_utc DATETIME(6) NULL AFTER entry_timestamp,
    ADD COLUMN exited_at_utc DATETIME(6) NULL AFTER exit_timestamp;

UPDATE user_roomvisits
SET entered_at_utc = CASE
        WHEN entry_timestamp IS NULL OR entry_timestamp <= 0 OR CAST(entry_timestamp AS DECIMAL(30, 6)) > 253402300799.999999 THEN NULL
        ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL entry_timestamp SECOND)
    END,
    exited_at_utc = CASE
        WHEN exit_timestamp IS NULL OR exit_timestamp <= 0 OR CAST(exit_timestamp AS DECIMAL(30, 6)) > 253402300799.999999 THEN NULL
        ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL exit_timestamp SECOND)
    END;

ALTER TABLE user_roomvisits
    DROP INDEX entry_timestamp,
    DROP INDEX exit_timestamp,
    DROP COLUMN entry_timestamp,
    DROP COLUMN exit_timestamp,
    CHANGE COLUMN entered_at_utc entry_timestamp DATETIME(6) NULL,
    CHANGE COLUMN exited_at_utc exit_timestamp DATETIME(6) NULL,
    ADD KEY entry_timestamp (entry_timestamp),
    ADD KEY exit_timestamp (exit_timestamp);
