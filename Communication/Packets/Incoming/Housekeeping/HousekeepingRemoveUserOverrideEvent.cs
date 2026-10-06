using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(PermissionKeys.HousekeepingRolesManage)]
internal sealed class HousekeepingRemoveUserOverrideEvent(IAccessControl access, IHousekeepingActionRunner runner) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var revision = packet.ReadInt();
        var userId = packet.ReadInt();
        var key = packet.ReadString();
        runner.Run(session, "permission.remove", PermissionKeys.HousekeepingRolesManage, actor =>
        {
            var result = access.Apply(actor, revision, new RemoveAccessOverride(userId, key));

            return new HousekeepingOutcome(result.Ok, result.Id, result.Message, HousekeepingTarget.Hotel, "permission.remove");
        });

        return Task.CompletedTask;
    }
}
