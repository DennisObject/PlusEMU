using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Navigator;

internal class CreateFlatEvent(IRoomCreationService rooms) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => rooms.Create(session, new(
        packet.ReadString(), packet.ReadString(), packet.ReadString(),
        packet.ReadInt(), packet.ReadInt(), packet.ReadInt()));
}
