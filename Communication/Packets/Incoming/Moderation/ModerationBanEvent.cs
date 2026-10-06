using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationBanSoft)]
internal class ModerationBanEvent : IPacketEvent
{
    private readonly IModerationSanctionService _sanctions;

    public ModerationBanEvent(IModerationSanctionService sanctions)
    {
        _sanctions = sanctions;
    }

    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        var message = packet.ReadString();
        var hours = packet.ReadInt();
        packet.ReadString(); //unk1
        packet.ReadString(); //unk2
        var ipBan = packet.ReadBool();
        var machineBan = packet.ReadBool();
        await _sanctions.Ban(session, new(userId, message, hours, ipBan, machineBan));
    }
}
