using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Pets;

internal sealed class PlacePetEvent(IPetPlacementService pets) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        pets.Place(room, session, packet.ReadInt(), packet.ReadInt(), packet.ReadInt());

        return Task.CompletedTask;
    }
}
