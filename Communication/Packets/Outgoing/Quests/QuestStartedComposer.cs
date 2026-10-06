using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Quests;

public sealed class QuestStartedComposer(QuestWireData quest) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.QuestStartedComposer;
    public void Compose(IOutgoingPacket packet) => QuestWireSerializer.Write(packet, quest);
}
