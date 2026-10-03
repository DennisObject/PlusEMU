-- Apply while PlusEMU is stopped; SQL updates are not automatic. Needs Resources/SQLs/Updates/13
-- (auth_ticket is no longer part of the primary key). Safe to re-run on MariaDB.

-- SSO tickets are single-use and short-lived. A consumed ticket is cleared to NULL.
ALTER TABLE `users` MODIFY `auth_ticket` varchar(60) NULL DEFAULT NULL;
ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `auth_ticket_expires_at` int(11) unsigned NULL DEFAULT NULL AFTER `auth_ticket`;

-- Tickets issued before this migration never expired and doubled as HTTP bearer tokens.
UPDATE `users` SET `auth_ticket` = NULL, `auth_ticket_expires_at` = NULL WHERE `auth_ticket` IS NOT NULL;

-- HTTP bearer tokens. Only the SHA-256 hex digest of a token is stored:
-- token_hash = SHA2(<token>, 256).
CREATE TABLE IF NOT EXISTS `user_access_tokens` (
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `user_id` int(11) NOT NULL,
    `token_hash` char(64) NOT NULL,
    `created_at` int(11) unsigned NOT NULL,
    `expires_at` int(11) unsigned NOT NULL,
    `revoked_at` int(11) unsigned NULL DEFAULT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `token_hash` (`token_hash`),
    KEY `user_id` (`user_id`),
    KEY `expires_at` (`expires_at`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;
