using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Quests;

public sealed class QuestCompletedComposer(QuestWireData quest) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.QuestCompletedComposer;
    public void Compose(IOutgoingPacket packet)
    {
        QuestWireSerializer.Write(packet, quest);
        packet.WriteBoolean(true);
    }
}
