using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Subscriptions;

namespace Plus.HabboHotel.Rooms.Chat.Styles;

public sealed class ChatStyle
{
    public ChatStyle(int id, string name, string requiredPermission, bool requiresHc = false, bool enabled = true)
    {
        Id = id;
        Name = name;
        RequiredPermission = requiredPermission;
        RequiresHc = requiresHc;
        Enabled = enabled;
    }

    public int Id { get; set; }
    public string Name { get; set; }
    public string RequiredPermission { get; set; }
    public bool RequiresHc { get; set; }
    public bool Enabled { get; set; }

    public bool CanUse(UserAccess access) => Enabled && (!RequiresHc || ClubAccess.LevelFor(access) > 0) &&
        (RequiredPermission.Length == 0 || access.Can(RequiredPermission));
}