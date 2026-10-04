using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(PermissionKeys.HousekeepingRolesManage)]
internal sealed class HousekeepingSaveRoleEvent(IAccessControl access, IHousekeepingActionRunner runner) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var revision = packet.ReadInt();
        var id = packet.ReadInt();
        var slug = packet.ReadString();
        var name = packet.ReadString();
        var description = packet.ReadString();
        var weight = packet.ReadInt();
        var securityLevel = packet.ReadInt();
        var badgeCode = packet.ReadString();
        var isStaff = packet.ReadBool();
        var isHidden = packet.ReadBool();
        runner.Run(session, "role.save", PermissionKeys.HousekeepingRolesManage, actor =>
        {
            var result = access.Apply(actor, revision, new SaveAccessRole(id, slug, name, description, weight, securityLevel, badgeCode, isStaff, isHidden));
            return new HousekeepingOutcome(result.Ok, result.Id, result.Message, HousekeepingTarget.Hotel, "role.save");
        });
        return Task.CompletedTask;
    }
}
