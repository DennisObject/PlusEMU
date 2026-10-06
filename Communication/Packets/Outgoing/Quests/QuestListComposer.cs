using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Quests;

public sealed class QuestListComposer(QuestListData data) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.QuestListComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(data.Quests.Length);

        foreach (var quest in data.Quests) {
            QuestWireSerializer.Write(packet, quest);
        }

        packet.WriteBoolean(data.Send);
    }
}
