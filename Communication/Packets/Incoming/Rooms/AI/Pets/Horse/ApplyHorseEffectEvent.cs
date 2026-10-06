using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Pets.Horse;

internal class ApplyHorseEffectEvent(IHorseCustomizationService horses) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        horses.ApplyEffect(room, session, packet.ReadUInt(), packet.ReadInt());

        return Task.CompletedTask;
    }
}
