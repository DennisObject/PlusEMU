using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(PermissionKeys.HousekeepingRolesManage)]
internal sealed class HousekeepingSetUserOverrideEvent(IAccessControl access, IHousekeepingActionRunner runner) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var revision = packet.ReadInt();
        var username = packet.ReadString();
        var key = packet.ReadString();
        var deny = packet.ReadBool();
        var reason = packet.ReadString();
        var expiresAt = packet.ReadInt();
        runner.Run(session, "permission.save", PermissionKeys.HousekeepingRolesManage, actor =>
        {
            var result = access.Apply(actor, revision, new SaveAccessOverride(username, key, deny, reason, expiresAt));
            return new HousekeepingOutcome(result.Ok, result.Id, result.Message, HousekeepingTarget.Hotel, "permission.save");
        });
        return Task.CompletedTask;
    }
}
