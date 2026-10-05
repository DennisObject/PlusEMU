using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Pets;

internal class RespectPetEvent(IPetRespectService respects) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var petId = packet.ReadInt();
        respects.Respect(room, session, petId);
        return Task.CompletedTask;
    }
}