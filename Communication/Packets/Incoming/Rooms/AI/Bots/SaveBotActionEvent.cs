using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Bots;

internal sealed class SaveBotActionEvent(IBotManagementService bots) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var request = new BotActionRequest(packet.ReadInt(), (BotAction)packet.ReadInt(), packet.ReadString());
        bots.SaveAction(session, request);

        return Task.CompletedTask;
    }
}
