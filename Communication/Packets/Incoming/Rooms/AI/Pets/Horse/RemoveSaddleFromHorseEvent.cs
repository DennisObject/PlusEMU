using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Pets.Horse;

internal class RemoveSaddleFromHorseEvent(IHorseCustomizationService horses) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        horses.RemoveSaddle(session, packet.ReadInt());
        return Task.CompletedTask;
    }
}
