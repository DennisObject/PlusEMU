using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Permissions;

public sealed partial class AccessControl
{
    private const int AdminPageSize = 25;
    private const string StaleRevision = "housekeeping.error.stale_revision";

    public AccessAdminResult Apply(Habbo actor, int expectedRevision, AccessAdminChange change)
    {
        lock (_sync)
        {
            using var connection = _database.Connection();

            if (actor.Id <= 0 || !Read(connection, actor.Id).Can(PermissionKeys.HousekeepingRolesManage))
            {
                return Rejected();
            }

            if (Revision(connection) != expectedRevision)
            {
                return Rejected(StaleRevision);
            }

            if (change is AssignAccessRole assign)
            {
                if (!ValidUsername(assign.Username) || assign.ExpiresAt < 0)
                {
                    return Rejected(HousekeepingErrors.InvalidInput);
                }

                var id = FindUser(connection, assign.Username);

                return id == 0 ? Rejected(HousekeepingErrors.UserNotFound) : Applied(AssignRole(actor, id, assign.RoleId, Expiry(assign.ExpiresAt)), id);
            }

            if (change is RevokeAccessRole revoke)
            {
                return Applied(RevokeRole(actor, revoke.UserId, revoke.RoleId), revoke.UserId);
            }

            if (change is SaveAccessOverride saveOverride)
            {
                if (!ValidUsername(saveOverride.Username) || saveOverride.ExpiresAt < 0 || string.IsNullOrWhiteSpace(saveOverride.Reason))
                {
                    return Rejected(HousekeepingErrors.InvalidInput);
                }

                var id = FindUser(connection, saveOverride.Username);

                return id == 0 ? Rejected(HousekeepingErrors.UserNotFound) : Applied(SetOverride(actor, id, saveOverride.Key, saveOverride.Deny, saveOverride.Reason, Expiry(saveOverride.ExpiresAt)), id);
            }

            if (change is RemoveAccessOverride removeOverride)
            {
                return Applied(RemoveOverride(actor, removeOverride.UserId, removeOverride.Key), removeOverride.UserId);
            }

            connection.Open();
            using var transaction = connection.BeginTransaction();

            if (connection.Query<int>("SELECT id FROM users WHERE id = @id FOR UPDATE", new
            {
                id = actor.Id
            }, transaction).SingleOrDefault() != actor.Id)
            {
                return Rejected();
            }

            var actorAccess = Read(connection, actor.Id, transaction);

            if (!actorAccess.Can(PermissionKeys.HousekeepingRolesManage))
            {
                return Rejected();
            }

            var roleId = change switch
            {
                SaveAccessRole save => save.Id,
                DeleteAccessRole delete => delete.RoleId,
                ChangeRolePermission permission => permission.RoleId,
                ChangeRoleLimit limit => limit.RoleId,
                _ => -1
            };
            var role = connection.QuerySingleOrDefault<AccessAdminRole>("SELECT id, slug, name, weight, is_staff AS IsStaff FROM roles WHERE id = @roleId FOR UPDATE", new
            {
                roleId
            }, transaction);

            if (roleId != 0 && (role == null || role.Weight >= actorAccess.Weight))
            {
                return Rejected();
            }

            string action;

            switch (change)
            {
                case SaveAccessRole save:
                    if (save.Id < 0 || save.Name.Length is < 1 or > 100 || string.IsNullOrWhiteSpace(save.Name) || save.Description.Length > 255 ||
                        save.BadgeCode.Length > 64 || save.Weight < 0 || save.SecurityLevel is < 0 or > 7 ||
                        save.Id == 0 && !Regex.IsMatch(save.Slug, "^[a-z][a-z0-9_-]{0,99}$"))
                    {
                        return Rejected(HousekeepingErrors.InvalidInput);
                    }

                    if (save.Weight >= actorAccess.Weight || save.SecurityLevel > actorAccess.SecurityLevel ||
                        save.IsStaff && role?.IsStaff != true && !actorAccess.Roles.Any(held => held.IsStaff))
                    {
                        return Rejected();
                    }

                    if (save.Id == 0)
                    {
                        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM roles WHERE slug = @Slug", save, transaction) != 0)
                        {
                            return Rejected(HousekeepingErrors.InvalidInput);
                        }

                        connection.Execute("INSERT INTO roles (slug, name, description, weight, security_level, badge_code, is_staff, is_hidden) " +
                            "VALUES (@Slug, @Name, @Description, @Weight, @SecurityLevel, @BadgeCode, @IsStaff, @IsHidden)", save, transaction);
                        roleId = connection.ExecuteScalar<int>("SELECT LAST_INSERT_ID()", transaction: transaction);
                        action = "role.create";
                    }
                    else
                    {
                        connection.Execute("UPDATE roles SET name = @Name, description = @Description, weight = @Weight, security_level = @SecurityLevel, " +
                            "badge_code = @BadgeCode, is_staff = @IsStaff, is_hidden = @IsHidden WHERE id = @Id", save, transaction);
                        action = "role.update";
                    }

                    break;
                case DeleteAccessRole:
                    if (role!.Slug == "default")
                    {
                        return Rejected();
                    }

                    connection.Execute("DELETE FROM roles WHERE id = @roleId", new
                    {
                        roleId
                    }, transaction);
                    action = "role.delete";
                    break;
                case ChangeRolePermission permission:
                    // Unknown grants are inert, but remain visible and removable after a registry change.
                    var orphan = !ValidPattern(permission.Key);

                    if (permission.Key.Length is < 1 or > 191 ||
                        (permission.Grant || !orphan) && !AccessMutationPolicy.CanGrant(actorAccess, permission.Key, _registry))
                    {
                        return Rejected();
                    }

                    if (permission.Grant)
                    {
                        connection.Execute("INSERT IGNORE INTO role_permissions (role_id, permission_key) VALUES (@RoleId, @Key)", permission, transaction);
                    }
                    else
                    {
                        connection.Execute("DELETE FROM role_permissions WHERE role_id = @RoleId AND permission_key = @Key", permission, transaction);
                    }

                    action = permission.Grant ? "role.permission.grant" : "role.permission.remove";
                    break;
                case ChangeRoleLimit limit:
                    if (!AccessLimits.Defaults.TryGetValue(limit.Key, out var fallback) || limit.Value < 0)
                    {
                        return Rejected(HousekeepingErrors.InvalidInput);
                    }

                    var resultingLimit = limit.Remove
                        ? connection.QuerySingleOrDefault<int?>("SELECT value FROM role_limits l JOIN roles r ON r.id = l.role_id WHERE r.slug = 'default' AND l.role_id <> @RoleId AND l.limit_key = @Key", limit, transaction) ?? fallback
                        : limit.Value;

                    if (resultingLimit > actorAccess.Limit(limit.Key, fallback))
                    {
                        return Rejected();
                    }

                    if (limit.Remove)
                    {
                        connection.Execute("DELETE FROM role_limits WHERE role_id = @RoleId AND limit_key = @Key", limit, transaction);
                    }
                    else
                    {
                        connection.Execute("INSERT INTO role_limits (role_id, limit_key, value) VALUES (@RoleId, @Key, @Value) ON DUPLICATE KEY UPDATE value = @Value", limit, transaction);
                    }

                    action = limit.Remove ? "role.limit.remove" : "role.limit.set";
                    break;
                default:
                    return Rejected(HousekeepingErrors.InvalidInput);
            }

            connection.Execute("INSERT INTO acl_audit_log (actor_id, action, target_type, target_id, payload) VALUES (@actorId, @action, 'role', @roleId, @payload)",
                new
                {
                    actorId = actor.Id,
                    action,
                    roleId,
                    payload = JsonSerializer.Serialize(new
                    {
                        targetName = (change as SaveAccessRole)?.Name ?? role!.Name,
                        change = JsonSerializer.SerializeToElement(change, change.GetType())
                    })
                }, transaction);
            transaction.Commit();
            // Init replaces the immutable role table under the same lock; Reload publishes the new rights.
            Reload();

            return new(true, roleId, "");
        }
    }

    public AccessAdminSnapshot AdminSnapshot(Habbo actor)
    {
        lock (_sync)
        {
            using var connection = _database.Connection();
            var access = RequireAdmin(connection, actor);
            var roles = connection.Query<AccessAdminRole>("SELECT r.id, r.slug, r.name, r.description, r.weight, r.security_level AS SecurityLevel, r.badge_code AS BadgeCode, " +
                "r.is_staff AS IsStaff, r.is_hidden AS IsHidden, IF(r.slug = 'default', (SELECT COUNT(*) FROM users), " +
                "(SELECT COUNT(*) FROM user_roles ur JOIN users u ON u.id = ur.user_id WHERE ur.role_id = r.id AND (ur.expires_at IS NULL OR ur.expires_at > @now))) AS MemberCount " +
                "FROM roles r ORDER BY weight DESC, id", new
                {
                    now = _clock.GetUtcNow().UtcDateTime
                }).ToArray();
            var grants = connection.Query<RolePermissionRow>("SELECT role_id AS RoleId, permission_key AS PermissionKey FROM role_permissions").ToArray();
            var limits = connection.Query<RoleLimitRow>("SELECT role_id AS RoleId, limit_key AS LimitKey, value FROM role_limits").ToArray();

            foreach (var role in roles)
            {
                role.Permissions = grants.Where(row => row.RoleId == role.Id).Select(row => row.PermissionKey).Order().ToArray();
                role.Limits = limits.Where(row => row.RoleId == role.Id).ToDictionary(row => row.LimitKey, row => row.Value);
            }

            var definitions = connection.Query<AccessPermissionDefinition>("SELECT `key`, category, description, is_orphan AS IsOrphan FROM acl_permissions ORDER BY category, `key`").ToList();

            // Wildcard and orphan patterns may exist only in role_permissions, without an acl_permissions row.
            foreach (var key in grants.Select(row => row.PermissionKey).Distinct().Except(definitions.Select(row => row.Key)))
            {
                definitions.Add(new()
                {
                    Key = key,
                    Category = "patterns",
                    IsOrphan = !ValidPattern(key)
                });
            }

            foreach (var key in new[] { "*" }.Concat(definitions.Where(row => !row.IsOrphan && !row.Key.Contains('*')).Select(row => row.Key.Split('.')[0] + ".*").Distinct()).ToArray())
            {
                if (ValidPattern(key) && definitions.All(row => row.Key != key))
                {
                    definitions.Add(new()
                    {
                        Key = key,
                        Category = "patterns"
                    });
                }
            }

            foreach (var definition in definitions)
            {
                definition.CanGrant = !definition.IsOrphan && AccessMutationPolicy.CanGrant(access, definition.Key, _registry);
            }

            return new(Revision(connection), access.Weight, roles, definitions.OrderBy(row => row.Category).ThenBy(row => row.Key).ToArray(),
                AccessLimits.Defaults.ToDictionary(pair => pair.Key, pair => access.Limit(pair.Key, pair.Value)));
        }
    }

    public AccessMemberPage Members(Habbo actor, int roleId, int offset)
    {
        lock (_sync)
        {
            using var connection = _database.Connection();
            RequireAdmin(connection, actor);
            offset = Math.Max(0, offset);
            var implicitRole = _roles.TryGetValue(roleId, out var role) && role.Slug == "default";
            var from = implicitRole ? "FROM users u" : "FROM user_roles ur JOIN users u ON u.id = ur.user_id WHERE ur.role_id = @roleId AND (ur.expires_at IS NULL OR ur.expires_at > @now)";
            var parameters = new
            {
                roleId,
                offset,
                limit = AdminPageSize,
                now = _clock.GetUtcNow().UtcDateTime
            };

            return new(roleId, offset, connection.ExecuteScalar<int>("SELECT COUNT(*) " + from, parameters),
                connection.Query<AccessMember>("SELECT u.id, u.username, " + (implicitRole ? "NULL" : "ur.expires_at") + " AS ExpiresAt " + from + " ORDER BY u.username, u.id LIMIT @limit OFFSET @offset", parameters).ToArray());
        }
    }

    public AccessOverridePage Overrides(Habbo actor, string username)
    {
        lock (_sync)
        {
            using var connection = _database.Connection();
            RequireAdmin(connection, actor);
            var user = ValidUsername(username) ? connection.QuerySingleOrDefault<AccessMember>("SELECT id, username FROM users WHERE username = @username", new
            {
                username
            }) : null;

            return user == null ? new(0, "", []) : new(user.Id, user.Username, connection.Query<AccessOverride>(
                "SELECT permission_key AS `Key`, effect, reason, expires_at AS ExpiresAt FROM user_permissions WHERE user_id = @id AND (expires_at IS NULL OR expires_at > @now) ORDER BY permission_key",
                new
                {
                    id = user.Id,
                    now = _clock.GetUtcNow().UtcDateTime
                }).ToArray());
        }
    }

    public AccessAuditPage Audit(Habbo actor, int offset)
    {
        lock (_sync)
        {
            using var connection = _database.Connection();
            RequireAdmin(connection, actor);
            offset = Math.Max(0, offset);

            return new(offset, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM acl_audit_log"), connection.Query<AccessAuditEntry>(
                "SELECT a.id, COALESCE(actor.username, 'System') AS ActorName, a.action, a.target_type AS TargetType, a.target_id AS TargetId, " +
                "COALESCE(target.username, r.name, JSON_UNQUOTE(JSON_EXTRACT(a.payload, '$.targetName')), CONCAT('#', a.target_id)) AS TargetName, a.payload, a.created_at AS CreatedAt " +
                "FROM acl_audit_log a LEFT JOIN users actor ON actor.id = a.actor_id LEFT JOIN users target ON a.target_type = 'user' AND target.id = a.target_id " +
                "LEFT JOIN roles r ON a.target_type = 'role' AND r.id = a.target_id ORDER BY a.id DESC LIMIT @limit OFFSET @offset", new
                {
                    offset,
                    limit = AdminPageSize
                }).ToArray());
        }
    }

    private UserAccess RequireAdmin(IDbConnection connection, Habbo actor)
    {
        var access = Read(connection, actor.Id);

        if (actor.Id <= 0 || !access.Can(PermissionKeys.HousekeepingRolesManage))
        {
            throw new UnauthorizedAccessException();
        }

        return access;
    }
    private static int Revision(IDbConnection connection) => connection.ExecuteScalar<int>("SELECT COALESCE(MAX(id), 0) FROM acl_audit_log");
    private static bool ValidUsername(string username) => username.Length is > 0 and <= 32 && !string.IsNullOrWhiteSpace(username);
    private static int FindUser(IDbConnection connection, string username) => connection.ExecuteScalar<int>("SELECT id FROM users WHERE username = @username", new { username });
    private static DateTimeOffset? Expiry(int value) => value == 0 ? null : DateTimeOffset.FromUnixTimeSeconds(value);
    private static AccessAdminResult Rejected(string message = HousekeepingErrors.Forbidden) => new(false, 0, message);
    private static AccessAdminResult Applied(bool ok, int id) => ok ? new(true, id, "") : Rejected();
}
