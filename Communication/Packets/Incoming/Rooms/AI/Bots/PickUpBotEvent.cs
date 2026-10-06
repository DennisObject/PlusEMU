using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Bots;

internal sealed class PickUpBotEvent(IBotManagementService bots) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        bots.PickUp(session, packet.ReadInt());
        return Task.CompletedTask;
    }
}
