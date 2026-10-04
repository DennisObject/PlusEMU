using System.Collections.Frozen;
using Plus.HabboHotel.Subscriptions;

namespace Plus.HabboHotel.Permissions;

// The inputs are immutable; only a compiled snapshot changes when its next expiry is reached.
public sealed class UserAccess
{
    private RoleAssignment[] _assignments;
    private UserPermissionOverride[] _overrides;
    private string[] _registry;
    private readonly TimeProvider _clock;
    private readonly object _sync = new();
    private Snapshot _snapshot;
    private ClubMembership _membership;
    internal long Now => _clock.GetUtcNow().ToUnixTimeSeconds();
    public ClubMembership Membership => Current.Membership;


    internal sealed record Snapshot(FrozenSet<string> Keys, FrozenDictionary<string, int> Limits,
        AccessRole[] Roles, AccessRole? PrimaryRole, int SecurityLevel, int Weight, DateTimeOffset? NextExpiry, ClubMembership Membership);

    private UserAccess(IEnumerable<RoleAssignment> roles, IEnumerable<UserPermissionOverride> overrides,
        IEnumerable<string> registry, TimeProvider clock, ClubMembership membership)
    {
        _assignments = roles.ToArray();
        _overrides = overrides.ToArray();
        _registry = registry.Distinct(StringComparer.Ordinal).ToArray();
        _clock = clock;
        _membership = membership;
        _snapshot = Compile(clock.GetUtcNow());
    }

    public static UserAccess Empty => Create(Array.Empty<RoleAssignment>());
    public static UserAccess Create(IEnumerable<RoleAssignment> roles, IEnumerable<UserPermissionOverride>? overrides = null,
        IEnumerable<string>? registry = null, TimeProvider? clock = null, ClubMembership? membership = null) =>
        new(roles, overrides ?? Array.Empty<UserPermissionOverride>(), registry ?? PermissionKeys.All.Select(p => p.Key), clock ?? TimeProvider.System, membership ?? ClubMembership.None);

    // Keep this holder stable across reloads: a login can retain it before its client is registered.
    internal void ReplaceWith(UserAccess replacement)
    {
        lock (_sync)
        {
            _assignments = replacement._assignments;
            _overrides = replacement._overrides;
            _registry = replacement._registry;
            _membership = replacement._membership;
            Volatile.Write(ref _snapshot, replacement.Capture());
        }
    }

    internal Snapshot Capture() => Current;
    public IReadOnlySet<string> Keys => Current.Keys;
    public IReadOnlyList<AccessRole> Roles => Current.Roles;
    public AccessRole? PrimaryRole => Current.PrimaryRole;
    public int SecurityLevel => Current.SecurityLevel;
    public int Weight => Current.Weight;
    internal DateTimeOffset? NextExpiry => Current.NextExpiry;
    internal bool HasExpired => Volatile.Read(ref _snapshot).NextExpiry is { } expiry && expiry <= _clock.GetUtcNow();
    public bool Can(string key) => Current.Keys.Contains(key);
    public int Limit(string key, int fallback = 0) => Current.Limits.GetValueOrDefault(key, fallback);
    public bool Outranks(UserAccess target) => Weight > target.Weight;

    private Snapshot Current
    {
        get
        {
            var snapshot = Volatile.Read(ref _snapshot);
            var now = _clock.GetUtcNow();
            if (snapshot.NextExpiry is not { } expiry || now < expiry) return snapshot;
            lock (_sync)
            {
                snapshot = _snapshot;
                if (snapshot.NextExpiry is { } next && now >= next)
                    Volatile.Write(ref _snapshot, snapshot = Compile(now));
                return snapshot;
            }
        }
    }

    internal static bool Matches(string pattern, string key) => pattern == "*" || pattern == key ||
        pattern.EndsWith(".*", StringComparison.Ordinal) && key.StartsWith(pattern[..^1], StringComparison.Ordinal);

    private Snapshot Compile(DateTimeOffset now)
    {
        var roles = _assignments.Where(assignment => assignment.ExpiresAt == null || assignment.ExpiresAt > now)
            .Select(assignment => assignment.Role).DistinctBy(role => role.Id).ToArray();
        var overrides = _overrides.Where(permission => permission.ExpiresAt == null || permission.ExpiresAt > now).ToArray();
        var grants = roles.SelectMany(role => role.Permissions).Concat(overrides.Where(permission => !permission.Deny).Select(permission => permission.Key)).ToArray();
        var denies = overrides.Where(permission => permission.Deny).Select(permission => permission.Key).ToArray();
        var keys = _registry.Where(key => grants.Any(pattern => Matches(pattern, key)) && !denies.Any(pattern => Matches(pattern, key)))
            .ToFrozenSet(StringComparer.Ordinal);
        var limits = roles.SelectMany(role => role.Limits).GroupBy(limit => limit.Key, StringComparer.Ordinal)
            .ToFrozenDictionary(group => group.Key, group => group.Max(limit => limit.Value), StringComparer.Ordinal);
        var primary = roles.OrderByDescending(role => role.Weight).ThenBy(role => role.Id).FirstOrDefault();
        var expiries = _assignments.Select(role => role.ExpiresAt).Concat(_overrides.Select(permission => permission.ExpiresAt))
            .Append(_membership.ExpiresAt > now.ToUnixTimeSeconds() ? DateTimeOffset.FromUnixTimeSeconds(_membership.ExpiresAt) : null)
            .Where(expiry => expiry > now).ToArray();
        return new(keys, limits, roles, primary, roles.Select(role => role.SecurityLevel).DefaultIfEmpty(1).Max(),
            roles.Select(role => role.Weight).DefaultIfEmpty(0).Max(), expiries.Length == 0 ? null : expiries.Min(), _membership);
    }
}
