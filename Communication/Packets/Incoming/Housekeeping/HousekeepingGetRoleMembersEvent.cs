using Plus.Communication.Attributes;
using Plus.Communication.Packets.Outgoing.Housekeeping;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(PermissionKeys.HousekeepingRolesManage)]
internal sealed class HousekeepingGetRoleMembersEvent(IAccessControl access) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (session.GetHabbo() is not { } actor || !actor.Access.Can(PermissionKeys.HousekeepingRolesManage)) {
            return Task.CompletedTask;
        }

        var requestId = packet.ReadInt();
        var roleId = packet.ReadInt();
        var offset = packet.ReadInt();
        session.Send(new HousekeepingRoleMembersComposer(requestId, access.Members(actor, roleId, offset)));

        return Task.CompletedTask;
    }
}
