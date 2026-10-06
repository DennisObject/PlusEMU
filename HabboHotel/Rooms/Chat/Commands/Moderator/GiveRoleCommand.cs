using Plus.Database;
using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal sealed class GiveRoleCommand(IDatabase database, IAccessControl access, TimeProvider clock) : RoleCommand(database, access, clock)
{
    public override string Key => "giverole";
    public override string Parameters => "%username% %roleSlug% [days]";
    public override string Description => "Assign a role to an online or offline user.";
    protected override bool Assign => true;
}
