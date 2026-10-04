using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(PermissionKeys.HousekeepingRolesManage)]
internal sealed class HousekeepingAssignRoleEvent(IAccessControl access, IHousekeepingActionRunner runner) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var revision = packet.ReadInt();
        var username = packet.ReadString();
        var roleId = packet.ReadInt();
        var expiresAt = packet.ReadInt();
        runner.Run(session, "role.assign", PermissionKeys.HousekeepingRolesManage, actor =>
        {
            var result = access.Apply(actor, revision, new AssignAccessRole(username, roleId, expiresAt));
            return new HousekeepingOutcome(result.Ok, result.Id, result.Message, HousekeepingTarget.Hotel, "role.assign");
        });
        return Task.CompletedTask;
    }
}
