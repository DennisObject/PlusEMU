namespace Plus.HabboHotel.Permissions;

public sealed record AccessRole(int Id, string Slug, string Name, int Weight, int SecurityLevel,
    string BadgeCode, bool IsStaff, IReadOnlyList<string> Permissions, IReadOnlyDictionary<string, int> Limits);
public sealed record RoleAssignment(AccessRole Role, DateTimeOffset? ExpiresAt = null);
public sealed record UserPermissionOverride(string Key, bool Deny, DateTimeOffset? ExpiresAt = null);
