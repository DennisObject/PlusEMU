-- Apply while PlusEMU is stopped; SQL updates are not automatic. Safe to re-run on MariaDB.

-- SSO tickets are single-use and short-lived. A consumed ticket is cleared to ''
-- (the stock dump declares auth_ticket NOT NULL).
ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `auth_ticket_expires_at` int(11) unsigned NULL DEFAULT NULL AFTER `auth_ticket`;
-- Set once the ticket has been traded for an HTTP access token (allowed once per ticket).
ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `auth_ticket_exchanged` tinyint(1) NOT NULL DEFAULT 0 AFTER `auth_ticket_expires_at`;

-- Tickets issued before this migration never expired and doubled as HTTP bearer tokens.
UPDATE `users` SET `auth_ticket` = '', `auth_ticket_expires_at` = NULL, `auth_ticket_exchanged` = 0 WHERE `auth_ticket` <> '';

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

-- "Remember me" tokens, also stored only as SHA-256 hex digests. Every use marks the row used
-- and adds its successor in the same family; presenting a used token revokes the family.
CREATE TABLE IF NOT EXISTS `user_remember_tokens` (
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `user_id` int(11) NOT NULL,
    `family_id` char(32) NOT NULL,
    `token_hash` char(64) NOT NULL,
    `created_at` int(11) unsigned NOT NULL,
    `expires_at` int(11) unsigned NOT NULL,
    `used_at` int(11) unsigned NULL DEFAULT NULL,
    `revoked_at` int(11) unsigned NULL DEFAULT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `token_hash` (`token_hash`),
    KEY `user_id` (`user_id`),
    KEY `family_id` (`family_id`),
    KEY `expires_at` (`expires_at`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;
