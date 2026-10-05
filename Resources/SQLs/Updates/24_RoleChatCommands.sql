-- Stop the emulator and apply after 23_HabboClubMembership.sql.
INSERT IGNORE INTO acl_permissions (`key`, category, description, is_orphan) VALUES
 ('command.giverole', 'command', 'Allows command giverole.', 0),
 ('command.takerole', 'command', 'Allows command takerole.', 0);

INSERT IGNORE INTO role_permissions (role_id, permission_key)
 SELECT role_id, 'command.giverole' FROM role_permissions WHERE permission_key = 'housekeeping.roles.manage';
INSERT IGNORE INTO role_permissions (role_id, permission_key)
 SELECT role_id, 'command.takerole' FROM role_permissions WHERE permission_key = 'housekeeping.roles.manage';
