-- Apply while PlusEMU is stopped; SQL updates are not automatic.
-- Audit trail of in-client housekeeping actions; the panel's audit tab reads it back.
CREATE TABLE IF NOT EXISTS housekeeping_log (
 id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
 `timestamp` INT NOT NULL,
 actor_id INT NOT NULL,
 actor_name VARCHAR(125) NOT NULL DEFAULT '',
 target_type VARCHAR(16) NOT NULL DEFAULT 'user',
 target_id INT NOT NULL DEFAULT 0,
 target_label VARCHAR(255) NOT NULL DEFAULT '',
 action VARCHAR(64) NOT NULL,
 detail VARCHAR(500) NOT NULL DEFAULT '',
 success TINYINT(1) NOT NULL DEFAULT 1,
 KEY timestamp_action (`timestamp`, action),
 KEY actor (actor_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Daily online peaks for the dashboard, written by the server status updater.
CREATE TABLE IF NOT EXISTS housekeeping_online_peaks (
 day DATE NOT NULL PRIMARY KEY,
 peak INT NOT NULL DEFAULT 0
) ENGINE=InnoDB;

-- acc_housekeeping opens the panel; each mutation also needs its own right.
INSERT INTO permissions (permission, description)
SELECT seed.permission, seed.description FROM (
 SELECT 'acc_housekeeping' AS permission, 'Can open the in-client housekeeping panel and its lookups.' AS description UNION ALL
 SELECT 'acc_soundboard_manage', 'Can manage the soundboard from the housekeeping panel.' UNION ALL
 SELECT 'housekeeping_sanction', 'Housekeeping: ban, unban, mute, kick, disconnect and trade lock lower ranks.' UNION ALL
 SELECT 'housekeeping_rank', 'Housekeeping: change lower ranks to a rank below your own.' UNION ALL
 SELECT 'housekeeping_password', 'Housekeeping: reset a lower rank''s password to a one-time password.' UNION ALL
 SELECT 'housekeeping_rooms', 'Housekeeping: open, close, mute and empty rooms of lower ranks.' UNION ALL
 SELECT 'housekeeping_room_ownership', 'Housekeeping: transfer and delete rooms of lower ranks.' UNION ALL
 SELECT 'housekeeping_economy', 'Housekeeping: give currency, furniture and Habbo Club to lower ranks.' UNION ALL
 SELECT 'housekeeping_alert', 'Housekeeping: send a hotel-wide alert.' UNION ALL
 SELECT 'housekeeping_private_data', 'Housekeeping: see email addresses and last IPs in user lookups.'
) AS seed
WHERE NOT EXISTS (SELECT 1 FROM permissions existing WHERE existing.permission = seed.permission);

-- Granted to the ranks that already hold mod_ban_any (Developer and Owner in the stock database).
-- Other ranks are left to the operator. Restart the emulator to load the new rights.
INSERT INTO permissions_rights (group_id, permission_id)
SELECT DISTINCT anchor_rights.group_id, granted.id
FROM permissions_rights anchor_rights
JOIN permissions anchor ON anchor.id = anchor_rights.permission_id AND anchor.permission = 'mod_ban_any'
JOIN permissions granted ON granted.permission IN ('acc_housekeeping', 'acc_soundboard_manage', 'housekeeping_sanction', 'housekeeping_rank',
 'housekeeping_password', 'housekeeping_rooms', 'housekeeping_room_ownership', 'housekeeping_economy', 'housekeeping_alert', 'housekeeping_private_data')
WHERE NOT EXISTS (SELECT 1 FROM permissions_rights existing WHERE existing.group_id = anchor_rights.group_id AND existing.permission_id = granted.id);
