using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

internal sealed class ApplyDecorationEvent(IRoomDecorationService decorations) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        decorations.Apply(room, session, new(packet.ReadUInt()));
        return Task.CompletedTask;
    }
}
