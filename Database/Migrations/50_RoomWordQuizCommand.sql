INSERT IGNORE INTO acl_permissions (`key`, category, description, is_orphan)
VALUES ('command.wordquiz', 'command', 'Start a timed yes/no question in an owned room.', FALSE);

INSERT IGNORE INTO role_permissions (role_id, permission_key)
SELECT id, 'command.wordquiz' FROM roles WHERE slug = 'default';
