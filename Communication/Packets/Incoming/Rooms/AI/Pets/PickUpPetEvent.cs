using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Pets;

internal sealed class PickUpPetEvent(IPetPlacementService pets) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        pets.PickUp(room, session, packet.ReadInt());
        return Task.CompletedTask;
    }
}
