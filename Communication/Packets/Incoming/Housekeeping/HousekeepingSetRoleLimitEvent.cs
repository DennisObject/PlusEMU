using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(PermissionKeys.HousekeepingRolesManage)]
internal sealed class HousekeepingSetRoleLimitEvent(IAccessControl access, IHousekeepingActionRunner runner) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var revision = packet.ReadInt();
        var roleId = packet.ReadInt();
        var key = packet.ReadString();
        var value = packet.ReadInt();
        var remove = packet.ReadBool();
        runner.Run(session, "role.limit", PermissionKeys.HousekeepingRolesManage, actor =>
        {
            var result = access.Apply(actor, revision, new ChangeRoleLimit(roleId, key, value, remove));
            return new HousekeepingOutcome(result.Ok, result.Id, result.Message, HousekeepingTarget.Hotel, "role.limit");
        });
        return Task.CompletedTask;
    }
}
