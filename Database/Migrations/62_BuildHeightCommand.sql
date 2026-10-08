INSERT IGNORE INTO acl_permissions (`key`, category, description, is_orphan)
VALUES ('command.bh', 'command', 'Place and move furniture at a fixed height.', FALSE);

INSERT IGNORE INTO role_permissions (role_id, permission_key)
SELECT id, 'command.bh' FROM roles WHERE slug = 'default';
