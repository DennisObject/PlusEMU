using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Bots;

internal class OpenBotActionEvent(IBotManagementService bots) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var botId = packet.ReadInt();
        var actionId = packet.ReadInt();
        bots.ShowAction(session, botId, actionId);

        return Task.CompletedTask;
    }
}
