using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Incoming.Quests;

internal sealed class GetCurrentQuestEvent(IQuestProgressService quests) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        quests.StartNext(session);

        return Task.CompletedTask;
    }
}
