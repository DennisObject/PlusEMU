using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Quests;

public class QuestAbortedComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.QuestAbortedComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteBoolean(false);
}
