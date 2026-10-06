using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Incoming.Quests;

internal sealed class StartQuestEvent(IQuestProgressService quests) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        quests.Start(session, packet.ReadInt());

        return Task.CompletedTask;
    }
}
