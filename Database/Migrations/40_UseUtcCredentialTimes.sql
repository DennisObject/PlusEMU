-- Refuse to reinterpret invalid security tombstones. A NULL tombstone means live/unused, so
-- silently mapping a bad non-NULL value to NULL could revive a credential.
SET @invalid_credential_tombstones = (
    SELECT COUNT(*) FROM (
        SELECT `revoked_at` AS value FROM `user_access_tokens` WHERE `revoked_at` IS NOT NULL
        UNION ALL SELECT `used_at` FROM `user_remember_tokens` WHERE `used_at` IS NOT NULL
        UNION ALL SELECT `revoked_at` FROM `user_remember_tokens` WHERE `revoked_at` IS NOT NULL
        UNION ALL SELECT `revoked_at` FROM `user_sessions` WHERE `revoked_at` IS NOT NULL
    ) credential_tombstones
    WHERE value <= 0 OR value > 253402300799.999999
);
SET @credential_preflight = IF(
    @invalid_credential_tombstones = 0,
    'DO 0',
    'SIGNAL SQLSTATE ''45000'' SET MESSAGE_TEXT = ''Invalid credential tombstone timestamp'''
);
PREPARE credential_preflight_statement FROM @credential_preflight;
EXECUTE credential_preflight_statement;
DEALLOCATE PREPARE credential_preflight_statement;

ALTER TABLE `users`
    ADD COLUMN `auth_ticket_expires_at_utc` DATETIME(6) NULL DEFAULT NULL AFTER `auth_ticket_expires_at`;
ALTER TABLE `user_access_tokens`
    ADD COLUMN `created_at_utc` DATETIME(6) NULL DEFAULT NULL AFTER `created_at`,
    ADD COLUMN `expires_at_utc` DATETIME(6) NULL DEFAULT NULL AFTER `expires_at`,
    ADD COLUMN `revoked_at_utc` DATETIME(6) NULL DEFAULT NULL AFTER `revoked_at`;
ALTER TABLE `user_remember_tokens`
    ADD COLUMN `created_at_utc` DATETIME(6) NULL DEFAULT NULL AFTER `created_at`,
    ADD COLUMN `expires_at_utc` DATETIME(6) NULL DEFAULT NULL AFTER `expires_at`,
    ADD COLUMN `used_at_utc` DATETIME(6) NULL DEFAULT NULL AFTER `used_at`,
    ADD COLUMN `revoked_at_utc` DATETIME(6) NULL DEFAULT NULL AFTER `revoked_at`;
ALTER TABLE `user_sessions`
    ADD COLUMN `created_at_utc` DATETIME(6) NULL DEFAULT NULL AFTER `created_at`,
    ADD COLUMN `revoked_at_utc` DATETIME(6) NULL DEFAULT NULL AFTER `revoked_at`;

UPDATE `users` SET `auth_ticket_expires_at_utc` = CASE
    WHEN `auth_ticket_expires_at` IS NULL OR `auth_ticket_expires_at` <= 0
        OR `auth_ticket_expires_at` > 253402300799.999999 THEN NULL
    ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`auth_ticket_expires_at` * 1000000) MICROSECOND)
END;
UPDATE `user_access_tokens` SET
    `created_at_utc` = CASE WHEN `created_at` <= 0 OR `created_at` > 253402300799.999999 THEN NULL ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`created_at` * 1000000) MICROSECOND) END,
    `expires_at_utc` = CASE WHEN `expires_at` <= 0 OR `expires_at` > 253402300799.999999 THEN NULL ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`expires_at` * 1000000) MICROSECOND) END,
    `revoked_at_utc` = CASE WHEN `revoked_at` IS NULL THEN NULL ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`revoked_at` * 1000000) MICROSECOND) END;
UPDATE `user_remember_tokens` SET
    `created_at_utc` = CASE WHEN `created_at` <= 0 OR `created_at` > 253402300799.999999 THEN NULL ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`created_at` * 1000000) MICROSECOND) END,
    `expires_at_utc` = CASE WHEN `expires_at` <= 0 OR `expires_at` > 253402300799.999999 THEN NULL ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`expires_at` * 1000000) MICROSECOND) END,
    `used_at_utc` = CASE WHEN `used_at` IS NULL THEN NULL ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`used_at` * 1000000) MICROSECOND) END,
    `revoked_at_utc` = CASE WHEN `revoked_at` IS NULL THEN NULL ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`revoked_at` * 1000000) MICROSECOND) END;
UPDATE `user_sessions` SET
    `created_at_utc` = CASE WHEN `created_at` <= 0 OR `created_at` > 253402300799.999999 THEN NULL ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`created_at` * 1000000) MICROSECOND) END,
    `revoked_at_utc` = CASE WHEN `revoked_at` IS NULL THEN NULL ELSE DATE_ADD(TIMESTAMP '1970-01-01 00:00:00', INTERVAL ROUND(`revoked_at` * 1000000) MICROSECOND) END;

ALTER TABLE `users`
    DROP COLUMN `auth_ticket_expires_at`,
    CHANGE COLUMN `auth_ticket_expires_at_utc` `auth_ticket_expires_at` DATETIME(6) NULL DEFAULT NULL AFTER `auth_ticket`;
ALTER TABLE `user_access_tokens`
    DROP INDEX `expires_at`,
    DROP COLUMN `created_at`, DROP COLUMN `expires_at`, DROP COLUMN `revoked_at`,
    CHANGE COLUMN `created_at_utc` `created_at` DATETIME(6) NULL DEFAULT NULL AFTER `token_hash`,
    CHANGE COLUMN `expires_at_utc` `expires_at` DATETIME(6) NULL DEFAULT NULL AFTER `created_at`,
    CHANGE COLUMN `revoked_at_utc` `revoked_at` DATETIME(6) NULL DEFAULT NULL AFTER `expires_at`,
    ADD KEY `expires_at` (`expires_at`);
ALTER TABLE `user_remember_tokens`
    DROP INDEX `expires_at`,
    DROP COLUMN `created_at`, DROP COLUMN `expires_at`, DROP COLUMN `used_at`, DROP COLUMN `revoked_at`,
    CHANGE COLUMN `created_at_utc` `created_at` DATETIME(6) NULL DEFAULT NULL AFTER `token_hash`,
    CHANGE COLUMN `expires_at_utc` `expires_at` DATETIME(6) NULL DEFAULT NULL AFTER `created_at`,
    CHANGE COLUMN `used_at_utc` `used_at` DATETIME(6) NULL DEFAULT NULL AFTER `expires_at`,
    CHANGE COLUMN `revoked_at_utc` `revoked_at` DATETIME(6) NULL DEFAULT NULL AFTER `grace_uses`,
    ADD KEY `expires_at` (`expires_at`);
ALTER TABLE `user_sessions`
    DROP INDEX `created_at`,
    DROP COLUMN `created_at`, DROP COLUMN `revoked_at`,
    CHANGE COLUMN `created_at_utc` `created_at` DATETIME(6) NULL DEFAULT NULL AFTER `user_id`,
    CHANGE COLUMN `revoked_at_utc` `revoked_at` DATETIME(6) NULL DEFAULT NULL AFTER `created_at`,
    ADD KEY `created_at` (`created_at`);
