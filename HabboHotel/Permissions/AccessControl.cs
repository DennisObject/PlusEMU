using Plus.Core;
using System.Collections.Frozen;
using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Subscriptions;

namespace Plus.HabboHotel.Permissions;

public sealed partial class AccessControl : IAccessControl, IDisposable, IStartable
{
    private readonly IDatabase _database;
    private readonly IGameClientManager _clients;
    private readonly ILogger<AccessControl> _logger;
    private readonly TimeProvider _clock;
    private readonly object _sync = new();
    private FrozenDictionary<int, AccessRole> _roles = new Dictionary<int, AccessRole>().ToFrozenDictionary();
    private string[] _registry = Array.Empty<string>();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, DateTimeOffset> _refreshAt = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, CachedAccess> _resolved = new();
    private ITimer? _expiryTimer;
    public event Action<Habbo>? AccessChanged;

    public AccessControl(IDatabase database, IGameClientManager clients, ILogger<AccessControl> logger, TimeProvider clock)
    {
        _database = database;
        _clients = clients;
        _logger = logger;
        _clock = clock;
    }

    public int StartOrder => 20;
    private const string RolesSql = "SELECT id, slug, name, weight, security_level AS SecurityLevel, badge_code AS BadgeCode, is_staff AS IsStaff FROM roles";
    private const string RolePermissionsSql = "SELECT role_id AS RoleId, permission_key AS PermissionKey FROM role_permissions";
    private const string RoleLimitsSql = "SELECT role_id AS RoleId, limit_key AS LimitKey, value FROM role_limits";
    private const string RegisterPermissionSql = "INSERT INTO acl_permissions (`key`, category, description, is_orphan) VALUES (@Key, @Category, @Description, 0) " +
        "ON DUPLICATE KEY UPDATE category = @Category, description = @Description, is_orphan = 0";

    public async Task Start()
    {
        using var connection = _database.Connection();
        var rows = (await connection.QueryAsync<RoleRow>(RolesSql).ConfigureAwait(false)).ToArray();
        var permissions = (await connection.QueryAsync<RolePermissionRow>(RolePermissionsSql).ConfigureAwait(false)).ToArray();
        var limits = (await connection.QueryAsync<RoleLimitRow>(RoleLimitsSql).ConfigureAwait(false)).ToArray();
        var definitions = Definitions(rows);
        if (connection is System.Data.Common.DbConnection asyncConnection)
            await asyncConnection.OpenAsync().ConfigureAwait(false);
        else
            connection.Open();
        using var transaction = connection.BeginTransaction();
        await connection.ExecuteAsync("UPDATE acl_permissions SET is_orphan = 1", transaction: transaction).ConfigureAwait(false);
        foreach (var permission in definitions)
            await connection.ExecuteAsync(RegisterPermissionSql, permission, transaction).ConfigureAwait(false);
        transaction.Commit();
        Publish(connection, rows, permissions, limits, definitions);
    }

    public void Init()
    {
        // Runtime mutations already hold this lock. Reload synchronously so publication stays on that thread.
        lock (_sync)
        {
            using var connection = _database.Connection();
            var rows = connection.Query<RoleRow>(RolesSql).ToArray();
            var permissions = connection.Query<RolePermissionRow>(RolePermissionsSql).ToArray();
            var limits = connection.Query<RoleLimitRow>(RoleLimitsSql).ToArray();
            var definitions = Definitions(rows);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            connection.Execute("UPDATE acl_permissions SET is_orphan = 1", transaction: transaction);
            foreach (var permission in definitions)
                connection.Execute(RegisterPermissionSql, permission, transaction);
            transaction.Commit();
            Publish(connection, rows, permissions, limits, definitions);
        }
    }

    private static PermissionDefinition[] Definitions(IEnumerable<RoleRow> rows) =>
        PermissionKeys.All.Concat(PermissionKeys.ForRoles(rows.Select(role => role.Slug))).DistinctBy(permission => permission.Key).ToArray();

    private void Publish(IDbConnection connection, RoleRow[] rows, RolePermissionRow[] permissions,
        RoleLimitRow[] limits, PermissionDefinition[] definitions)
    {
        var registry = definitions.Select(permission => permission.Key).ToArray();
        var roles = rows.ToFrozenDictionary(role => role.Id, role => new AccessRole(role.Id, role.Slug, role.Name, role.Weight,
            role.SecurityLevel, role.BadgeCode, role.IsStaff, permissions.Where(p => p.RoleId == role.Id).Select(p => p.PermissionKey).ToArray(),
            limits.Where(limit => limit.RoleId == role.Id).ToFrozenDictionary(limit => limit.LimitKey, limit => limit.Value, StringComparer.Ordinal)));
        if (!roles.Values.Any(role => role.Slug == "default")) throw new InvalidOperationException("The default access role is missing. Apply the RBAC migration.");
        lock (_sync)
        {
            _registry = registry;
            _roles = roles;
            foreach (var entry in _resolved.Values.ToArray())
                if (!ReferenceEquals(GetOnlineHabbo(entry.Habbo.Id), entry.Habbo)) Evict(entry);
            Prune(connection);
            foreach (var client in _clients.GetClients.ToArray())
                if (client.GetHabbo() is { AccessClosed: false } habbo) Resolve(habbo.Id);
            _expiryTimer ??= _clock.CreateTimer(_ => RefreshExpired(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            _logger.LogInformation("Loaded {Roles} access roles and {Permissions} permission keys", _roles.Count, _registry.Length);
        }
    }

    public UserAccess Resolve(int userId)
    {
        lock (_sync)
        {
            using var connection = _database.Connection();
            var access = Read(connection, userId);
            var habbo = GetOnlineHabbo(userId);
            if (_resolved.TryGetValue(userId, out var current) && !ReferenceEquals(current.Habbo, habbo))
            {
                Evict(current);
                current = null;
            }
            if (habbo == null) return access;
            if (current != null)
            {
                current.Access.ReplaceWith(access);
                access = current.Access;
            }
            else
            {
                habbo.Disconnected += OnAccessEnded;
                habbo.Disposed += OnAccessEnded;
                if (habbo.AccessClosed)
                {
                    habbo.Disconnected -= OnAccessEnded;
                    habbo.Disposed -= OnAccessEnded;
                    return access;
                }
                _resolved[userId] = new(habbo, access);
            }
            habbo.Access = access;
            // Reads must not cancel an expiry that has yet to be published to the client.
            var next = access.NextExpiry ?? DateTimeOffset.MaxValue;
            _refreshAt.AddOrUpdate(userId, next, (_, pending) => pending < next ? pending : next);
            return access;
        }
    }

    public void Reload()
    {
        Init();
        lock (_sync)
            foreach (var client in _clients.GetClients.ToArray())
                if (client.GetHabbo() is { AccessClosed: false } habbo) Publish(habbo.Id, GetAccess(habbo.Id));
    }

    public void Refresh(int userId)
    {
        lock (_sync)
        {
            var access = Resolve(userId);
            using (var connection = _database.Connection())
            {
                connection.Execute("UPDATE users SET `rank` = @securityLevel WHERE id = @userId", new { userId, securityLevel = access.SecurityLevel });
                Prune(connection, userId);
            }
            Publish(userId, access);
        }
    }


    private Habbo? GetOnlineHabbo(int userId) => _clients.GetClientByUserId(userId)?.GetHabbo() is { AccessClosed: false } habbo ? habbo : null;

    private UserAccess GetAccess(int userId) => _resolved.TryGetValue(userId, out var entry) &&
        ReferenceEquals(GetOnlineHabbo(userId), entry.Habbo) ? entry.Access : Resolve(userId);

    private void Publish(int userId, UserAccess access)
    {
        var client = _clients.GetClientByUserId(userId);
        if (client?.GetHabbo() is not { AccessClosed: false } habbo) return;
        habbo.Access = access;
        client.Send(new UserRightsComposer(access));
        AccessChanged?.Invoke(habbo);
        _refreshAt[userId] = access.NextExpiry ?? DateTimeOffset.MaxValue;
    }

    private void OnAccessEnded(object? sender, EventArgs args)
    {
        lock (_sync)
            if (sender is Habbo habbo && _resolved.TryGetValue(habbo.Id, out var entry) && ReferenceEquals(entry.Habbo, habbo)) Evict(entry);
    }

    private void Evict(CachedAccess entry)
    {
        _resolved.TryRemove(entry.Habbo.Id, out _);
        _refreshAt.TryRemove(entry.Habbo.Id, out _);
        entry.Habbo.Disconnected -= OnAccessEnded;
        entry.Habbo.Disposed -= OnAccessEnded;
    }

    public bool Can(int userId, string key) => GetAccess(userId).Can(key);
    public int Limit(int userId, string key, int fallback = 0) => GetAccess(userId).Limit(key, fallback);
    public bool Outranks(int actorId, int targetId) => actorId != targetId && GetAccess(actorId).Outranks(GetAccess(targetId));
    public bool TryGetRole(int roleId, out AccessRole role)
    {
        lock (_sync) return _roles.TryGetValue(roleId, out role!);
    }


    public bool AssignRole(Habbo actor, int targetId, int roleId, DateTimeOffset? expiresAt = null) =>
        Mutate(actor, targetId, "role.assign", new { roleId, expiresAt }, (connection, transaction, actorAccess, targetAccess) =>
        {
            if (!TryGetRole(roleId, out var role) || role.Slug == "default" ||
                !AccessMutationPolicy.CanAssign(actor.Id, targetId, actorAccess, targetAccess, role, _registry) ||
                expiresAt <= _clock.GetUtcNow()) return false;
            connection.Execute("INSERT INTO user_roles (user_id, role_id, granted_by, expires_at) VALUES (@targetId, @roleId, @actorId, @expiresAt) " +
                "ON DUPLICATE KEY UPDATE granted_by = @actorId, expires_at = @expiresAt", new { targetId, roleId, actorId = actor.Id, expiresAt = expiresAt?.UtcDateTime }, transaction);
            return true;
        });

    public bool RevokeRole(Habbo actor, int targetId, int roleId) =>
        Mutate(actor, targetId, "role.revoke", new { roleId }, (connection, transaction, actorAccess, targetAccess) =>
        {
            if (!TryGetRole(roleId, out var role) || role.Slug == "default" || role.Weight >= actorAccess.Weight) return false;
            return connection.Execute("DELETE FROM user_roles WHERE user_id = @targetId AND role_id = @roleId", new { targetId, roleId }, transaction) > 0;
        });

    public bool SetOverride(Habbo actor, int targetId, string key, bool deny, string reason, DateTimeOffset? expiresAt = null) =>
        Mutate(actor, targetId, deny ? "permission.deny" : "permission.grant", new { key, reason, expiresAt }, (connection, transaction, actorAccess, _) =>
        {
            if (reason.Length > 512 || expiresAt <= _clock.GetUtcNow() || !ValidPattern(key) ||
                !deny && !AccessMutationPolicy.CanGrant(actorAccess, key, _registry)) return false;
            connection.Execute("INSERT INTO user_permissions (user_id, permission_key, effect, granted_by, reason, expires_at) " +
                "VALUES (@targetId, @key, @effect, @actorId, @reason, @expiresAt) ON DUPLICATE KEY UPDATE effect = @effect, granted_by = @actorId, reason = @reason, expires_at = @expiresAt",
                new { targetId, key, effect = deny ? "deny" : "grant", actorId = actor.Id, reason, expiresAt = expiresAt?.UtcDateTime }, transaction);
            return true;
        });

    public bool RemoveOverride(Habbo actor, int targetId, string key) =>
        Mutate(actor, targetId, "permission.remove", new { key }, (connection, transaction, actorAccess, _) =>
        {
            var effect = connection.QuerySingleOrDefault<string>("SELECT effect FROM user_permissions WHERE user_id = @targetId AND permission_key = @key", new { targetId, key }, transaction);
            if (effect == null || effect == "deny" && !AccessMutationPolicy.CanGrant(actorAccess, key, _registry)) return false;
            return connection.Execute("DELETE FROM user_permissions WHERE user_id = @targetId AND permission_key = @key", new { targetId, key }, transaction) > 0;
        });

    private bool Mutate(Habbo actor, int targetId, string action, object payload, Func<IDbConnection, IDbTransaction, UserAccess, UserAccess, bool> mutation)
    {
        lock (_sync)
        {
            if (actor.Id == targetId || actor.Id <= 0 || targetId <= 0) return false;
            using var connection = _database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            // Serialize authorization with writes to either account, including concurrent administrative mutations.
            var users = connection.Query<int>("SELECT id FROM users WHERE id IN @ids ORDER BY id FOR UPDATE", new { ids = new[] { actor.Id, targetId } }, transaction).ToArray();
            if (users.Length != 2) return false;
            var actorAccess = Read(connection, actor.Id, transaction);
            var targetAccess = Read(connection, targetId, transaction);
            if (!AccessMutationPolicy.CanEdit(actor.Id, targetId, actorAccess, targetAccess) || !mutation(connection, transaction, actorAccess, targetAccess)) return false;
            var resolved = Read(connection, targetId, transaction);
            connection.Execute("UPDATE users SET `rank` = @securityLevel WHERE id = @targetId", new { targetId, securityLevel = resolved.SecurityLevel }, transaction);
            connection.Execute("INSERT INTO acl_audit_log (actor_id, action, target_type, target_id, payload) VALUES (@actorId, @action, 'user', @targetId, @payload)",
                new { actorId = actor.Id, action, targetId, payload = JsonSerializer.Serialize(payload) }, transaction);
            transaction.Commit();
            Refresh(targetId);
            return true;
        }
    }

    private bool ValidPattern(string pattern) => pattern.Length <= 191 &&
        (pattern == "*" || _registry.Contains(pattern, StringComparer.Ordinal) || pattern.EndsWith(".*", StringComparison.Ordinal) && _registry.Any(key => UserAccess.Matches(pattern, key)));

    private UserAccess Read(IDbConnection connection, int userId, IDbTransaction? transaction = null)
    {
        var assignments = new List<RoleAssignment> { new(_roles.Values.Single(role => role.Slug == "default")) };
        foreach (var row in connection.Query<AssignmentRow>("SELECT role_id AS RoleId, expires_at AS ExpiresAt FROM user_roles WHERE user_id = @userId", new { userId }, transaction))
            if (_roles.TryGetValue(row.RoleId, out var role) && role.Slug != "default")
                assignments.Add(new(role, row.ExpiresAt));
        var overrides = connection.Query<OverrideRow>("SELECT permission_key AS PermissionKey, effect, expires_at AS ExpiresAt FROM user_permissions WHERE user_id = @userId", new { userId }, transaction)
            .Select(row => new UserPermissionOverride(row.PermissionKey, row.Effect == "deny", row.ExpiresAt));
        var membership = connection.QuerySingleOrDefault<ClubMembership>("SELECT " + ClubMembership.Columns + " FROM user_club_memberships WHERE user_id = @userId", new { userId }, transaction);
        return UserAccess.Create(assignments, overrides, _registry, _clock, membership);
    }

    private void Prune(IDbConnection connection, int? userId = null)
    {
        var filter = userId.HasValue ? " AND user_id = @userId" : string.Empty;
        var parameters = new { now = _clock.GetUtcNow().UtcDateTime, userId };
        if (connection.State != ConnectionState.Open) connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("INSERT INTO acl_audit_log (actor_id, action, target_type, target_id, payload) " +
            "SELECT NULL, 'role.expire', 'user', user_id, JSON_OBJECT('roleId', role_id) FROM user_roles WHERE expires_at <= @now" + filter,
            parameters, transaction);
        connection.Execute("INSERT INTO acl_audit_log (actor_id, action, target_type, target_id, payload) " +
            "SELECT NULL, 'permission.expire', 'user', user_id, JSON_OBJECT('key', permission_key, 'effect', effect) FROM user_permissions WHERE expires_at <= @now" + filter,
            parameters, transaction);
        connection.Execute("DELETE FROM user_roles WHERE expires_at <= @now" + filter, parameters, transaction);
        connection.Execute("DELETE FROM user_permissions WHERE expires_at <= @now" + filter, parameters, transaction);
        if (!userId.HasValue)
            connection.Execute("UPDATE users u SET u.`rank` = GREATEST(@defaultSecurity, COALESCE((SELECT MAX(r.security_level) FROM user_roles ur JOIN roles r ON r.id = ur.role_id " +
                "WHERE ur.user_id = u.id AND (ur.expires_at IS NULL OR ur.expires_at > @now)), @defaultSecurity))",
                new { parameters.now, defaultSecurity = _roles.Values.Single(role => role.Slug == "default").SecurityLevel }, transaction);
        transaction.Commit();
    }

    private void RefreshExpired()
    {
        try
        {
            var now = _clock.GetUtcNow();
            foreach (var client in _clients.GetClients.ToArray())
                if (client.GetHabbo() is { AccessClosed: false } habbo && _refreshAt.TryGetValue(habbo.Id, out var expiry) && expiry <= now) Refresh(habbo.Id);
        }
        catch (Exception exception) { _logger.LogError(exception, "Refreshing expired access failed"); }
    }

    public void Dispose()
    {
        _expiryTimer?.Dispose();
        lock (_sync)
            foreach (var entry in _resolved.Values.ToArray()) Evict(entry);
    }
    private sealed record CachedAccess(Habbo Habbo, UserAccess Access);
    private sealed class RoleRow { public int Id { get; set; } public string Slug { get; set; } = ""; public string Name { get; set; } = ""; public int Weight { get; set; } public int SecurityLevel { get; set; } public string BadgeCode { get; set; } = ""; public bool IsStaff { get; set; } }
    private sealed class RolePermissionRow { public int RoleId { get; set; } public string PermissionKey { get; set; } = ""; }
    private sealed class RoleLimitRow { public int RoleId { get; set; } public string LimitKey { get; set; } = ""; public int Value { get; set; } }
    private sealed class AssignmentRow { public int RoleId { get; set; } public DateTimeOffset? ExpiresAt { get; set; } }
    private sealed class OverrideRow { public string PermissionKey { get; set; } = ""; public string Effect { get; set; } = ""; public DateTimeOffset? ExpiresAt { get; set; } }
}
