using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal sealed class UpdateGroupColoursEvent(IGroupAppearanceService appearance) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => appearance.UpdateColours(session,
        new(packet.ReadInt(), packet.ReadInt(), packet.ReadInt()));
}
