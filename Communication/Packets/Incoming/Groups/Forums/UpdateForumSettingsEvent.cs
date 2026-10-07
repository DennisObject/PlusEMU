using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Incoming.Groups.Forums;

internal sealed class UpdateForumSettingsEvent(IGroupForumService service) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        service.UpdateSettings(session, packet.ReadInt(), new ForumPermissions(packet.ReadInt(), packet.ReadInt(), packet.ReadInt(), packet.ReadInt()));

        return Task.CompletedTask;
    }
}
