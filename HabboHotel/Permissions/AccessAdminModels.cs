namespace Plus.HabboHotel.Permissions;

public abstract record AccessAdminChange;
public sealed record SaveAccessRole(int Id, string Slug, string Name, string Description, int Weight, int SecurityLevel, string BadgeCode, bool IsStaff, bool IsHidden) : AccessAdminChange;
public sealed record DeleteAccessRole(int RoleId) : AccessAdminChange;
public sealed record ChangeRolePermission(int RoleId, string Key, bool Grant) : AccessAdminChange;
public sealed record ChangeRoleLimit(int RoleId, string Key, int Value, bool Remove) : AccessAdminChange;
public sealed record AssignAccessRole(string Username, int RoleId, int ExpiresAt) : AccessAdminChange;
public sealed record RevokeAccessRole(int UserId, int RoleId) : AccessAdminChange;
public sealed record SaveAccessOverride(string Username, string Key, bool Deny, string Reason, int ExpiresAt) : AccessAdminChange;
public sealed record RemoveAccessOverride(int UserId, string Key) : AccessAdminChange;
public sealed record AccessAdminResult(bool Ok, int Id, string Message);

public sealed class AccessAdminRole
{
    public int Id
    {
        get; set;
    }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Weight
    {
        get; set;
    }
    public int SecurityLevel
    {
        get; set;
    }
    public string BadgeCode { get; set; } = "";
    public bool IsStaff
    {
        get; set;
    }
    public bool IsHidden
    {
        get; set;
    }
    public int MemberCount
    {
        get; set;
    }
    public string[] Permissions { get; set; } = [];
    public Dictionary<string, int> Limits { get; set; } = new();
}

public sealed class AccessPermissionDefinition
{
    public string Key { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsOrphan
    {
        get; set;
    }
    public bool CanGrant
    {
        get; set;
    }
}

public sealed record AccessAdminSnapshot(int Revision, int ActorWeight, IReadOnlyList<AccessAdminRole> Roles, IReadOnlyList<AccessPermissionDefinition> Permissions, IReadOnlyDictionary<string, int> Limits);
public sealed class AccessMember
{
    public int Id
    {
        get; set;
    }
    public string Username { get; set; } = "";
    public DateTimeOffset? ExpiresAt
    {
        get; set;
    }
}
public sealed record AccessMemberPage(int RoleId, int Offset, int Total, IReadOnlyList<AccessMember> Members);
public sealed class AccessOverride
{
    public string Key { get; set; } = "";
    public string Effect { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset? ExpiresAt
    {
        get; set;
    }
}
public sealed record AccessOverridePage(int UserId, string Username, IReadOnlyList<AccessOverride> Overrides);
public sealed class AccessAuditEntry
{
    public int Id
    {
        get; set;
    }
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public string TargetType { get; set; } = "";
    public int TargetId
    {
        get; set;
    }
    public string TargetName { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTimeOffset? CreatedAt
    {
        get; set;
    }
}
public sealed record AccessAuditPage(int Offset, int Total, IReadOnlyList<AccessAuditEntry> Entries);
