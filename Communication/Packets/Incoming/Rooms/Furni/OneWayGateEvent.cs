using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal sealed class OneWayGateEvent(IFurnitureUseService furniture) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        furniture.UseOneWayGate(room, session, packet.ReadUInt());
        return Task.CompletedTask;
    }
}