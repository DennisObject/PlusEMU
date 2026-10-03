-- Apply while PlusEMU is stopped; SQL updates are not automatic. Written for MariaDB.
-- Octane's in-client catalog editor and furni editor. Every staff mutation is written to an
-- audit table in the same transaction as the change. catalog_admin_log.id is also the catalog
-- revision the editor uses to detect stale saves.
CREATE TABLE IF NOT EXISTS catalog_admin_log (
 id INT NOT NULL AUTO_INCREMENT,
 user_id INT NOT NULL,
 username VARCHAR(50) NOT NULL,
 action VARCHAR(32) NOT NULL,
 entity_type ENUM('PAGE','OFFER') NOT NULL,
 catalog_type ENUM('NORMAL','BUILDER') NOT NULL,
 entity_id INT NOT NULL,
 operation VARCHAR(16) NOT NULL,
 summary VARCHAR(255) NOT NULL DEFAULT '',
 before_json MEDIUMTEXT NULL,
 after_json MEDIUMTEXT NULL,
 created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
 PRIMARY KEY (id),
 KEY idx_catalog_admin_log_entity (entity_type, entity_id),
 KEY idx_catalog_admin_log_user (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- furnidata_update rows hold the whole furnidata entry before and after; revert restores the newest
-- unreverted one for the item and marks it reverted.
CREATE TABLE IF NOT EXISTS furni_editor_log (
 id INT NOT NULL AUTO_INCREMENT,
 user_id INT NOT NULL,
 username VARCHAR(50) NOT NULL,
 action ENUM('update','delete','furnidata_update','furnidata_revert') NOT NULL,
 item_id INT UNSIGNED NOT NULL,
 classname VARCHAR(70) NOT NULL DEFAULT '',
 before_json MEDIUMTEXT NULL,
 after_json MEDIUMTEXT NULL,
 reverted TINYINT(1) NOT NULL DEFAULT 0,
 created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
 PRIMARY KEY (id),
 KEY idx_furni_editor_log_item (item_id, action, reverted),
 KEY idx_furni_editor_log_user (user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT INTO permissions (permission, description)
SELECT 'acc_catalogfurni', 'Can use the in-client catalog editor and furni editor.'
WHERE NOT EXISTS (SELECT 1 FROM permissions WHERE permission = 'acc_catalogfurni');
INSERT INTO permissions (permission, description)
SELECT 'acc_furnidata_edit', 'Can edit and revert the shared FurnitureData.json from the furni editor.'
WHERE NOT EXISTS (SELECT 1 FROM permissions WHERE permission = 'acc_furnidata_edit');
INSERT INTO permissions (permission, description)
SELECT 'acc_furni_delete', 'Can delete unused furniture definitions from the furni editor.'
WHERE NOT EXISTS (SELECT 1 FROM permissions WHERE permission = 'acc_furni_delete');

-- Granted like the other high-power staff rights: Developer (8) and Owner (9) by default.
-- Hotels with other rank ids set @catalog_admin_min_rank before running.
SET @catalog_admin_min_rank = COALESCE(@catalog_admin_min_rank, 8);
INSERT INTO permissions_rights (group_id, permission_id)
SELECT g.id, p.id FROM permissions_groups g
JOIN permissions p ON p.permission IN ('acc_catalogfurni', 'acc_furnidata_edit', 'acc_furni_delete')
WHERE g.id >= @catalog_admin_min_rank
 AND NOT EXISTS (SELECT 1 FROM permissions_rights r WHERE r.group_id = g.id AND r.permission_id = p.id);
