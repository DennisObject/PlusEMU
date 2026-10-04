-- Apply while PlusEMU is stopped; SQL updates are not automatic. Safe to re-run on MariaDB.

-- SSO tickets are single-use and short-lived. A consumed ticket is cleared to ''
-- (the stock dump declares auth_ticket NOT NULL).
ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `auth_ticket_expires_at` int(11) unsigned NULL DEFAULT NULL AFTER `auth_ticket`;
-- Bumped whenever all of a user's credentials are revoked; logins that started before it
-- changed write nothing.
ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `credential_generation` int(11) unsigned NOT NULL DEFAULT 0;
-- The login session (user_sessions.id) the current ticket belongs to.
ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `auth_ticket_session` char(32) NULL DEFAULT NULL;
-- Set once the ticket has been traded for an HTTP access token (allowed once per ticket).
ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `auth_ticket_exchanged` tinyint(1) NOT NULL DEFAULT 0 AFTER `auth_ticket_expires_at`;

-- Tickets issued before this migration never expired and doubled as HTTP bearer tokens.
UPDATE `users` SET `auth_ticket` = '', `auth_ticket_expires_at` = NULL, `auth_ticket_exchanged` = 0, `auth_ticket_session` = NULL WHERE `auth_ticket` <> '';

-- One row per login (device). Logout revokes a session: its ticket, access tokens and
-- remember family; credentials are only written while their session is not revoked.
CREATE TABLE IF NOT EXISTS `user_sessions` (
    `id` char(32) NOT NULL,
    `user_id` int(11) NOT NULL,
    `created_at` int(11) unsigned NOT NULL,
    `revoked_at` int(11) unsigned NULL DEFAULT NULL,
    PRIMARY KEY (`id`),
    KEY `user_id` (`user_id`),
    KEY `created_at` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- HTTP bearer tokens. Only the SHA-256 hex digest of a token is stored:
-- token_hash = SHA2(<token>, 256).
CREATE TABLE IF NOT EXISTS `user_access_tokens` (
    `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
    `user_id` int(11) NOT NULL,
    `session_id` char(32) NULL DEFAULT NULL,
    `token_hash` char(64) NOT NULL,
    `created_at` int(11) unsigned NOT NULL,
    `expires_at` int(11) unsigned NOT NULL,
    `revoked_at` int(11) unsigned NULL DEFAULT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `token_hash` (`token_hash`),
    KEY `user_id` (`user_id`),
    KEY `session_id` (`session_id`),
    KEY `expires_at` (`expires_at`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

-- "Remember me" tokens, also stored only as SHA-256 hex digests. family_id is the login's
-- user_sessions.id. Every use marks the row used and adds its successor in the same family;
-- presenting a used token signs the account out everywhere.
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
