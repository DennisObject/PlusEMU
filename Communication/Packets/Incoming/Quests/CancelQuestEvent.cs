using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Incoming.Quests;

internal sealed class CancelQuestEvent(IQuestProgressService quests) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        quests.Cancel(session);

        return Task.CompletedTask;
    }
}
