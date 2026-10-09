ALTER TABLE rooms ADD COLUMN IF NOT EXISTS hide_wired TINYINT(1) NOT NULL DEFAULT 0;

INSERT IGNORE INTO acl_permissions (`key`, category, description, is_orphan)
VALUES ('command.hidewired', 'command', 'Hide or show the wired furniture in an owned room.', FALSE);

INSERT IGNORE INTO role_permissions (role_id, permission_key)
SELECT id, 'command.hidewired' FROM roles WHERE slug = 'default';
