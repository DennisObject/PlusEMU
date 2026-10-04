using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Permissions;

public interface IAccessControl
{
    void Init();
    bool Can(int userId, string key);
    int Limit(int userId, string key, int fallback = 0);
    UserAccess Resolve(int userId);
    void Reload();
    void Refresh(int userId);
    bool Outranks(int actorId, int targetId);
    bool TryGetRole(int roleId, out AccessRole role);
    bool AssignRole(Habbo actor, int targetId, int roleId, DateTimeOffset? expiresAt = null);
    bool ReplaceRoles(Habbo actor, int targetId, int roleId);
    bool RevokeRole(Habbo actor, int targetId, int roleId);
    bool SetOverride(Habbo actor, int targetId, string key, bool deny, string reason, DateTimeOffset? expiresAt = null);
    bool RemoveOverride(Habbo actor, int targetId, string key);
}
