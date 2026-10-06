using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

internal class PlaceObjectEvent(IRoomItemPlacementService placement) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        placement.Place(room, session, packet.ReadString());

        return Task.CompletedTask;
    }
}
