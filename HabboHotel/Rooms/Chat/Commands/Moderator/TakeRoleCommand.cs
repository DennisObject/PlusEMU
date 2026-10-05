using Plus.Database;
using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal sealed class TakeRoleCommand(IDatabase database, IAccessControl access, TimeProvider clock) : RoleCommand(database, access, clock)
{
    public override string Key => "takerole";
    public override string Parameters => "%username% %roleSlug%";
    public override string Description => "Revoke a role from an online or offline user.";
    protected override bool Assign => false;
}
