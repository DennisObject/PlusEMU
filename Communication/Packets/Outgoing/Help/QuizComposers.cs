using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Help;

public sealed class QuizDataComposer(string code, ImmutableArray<int> questionIds) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.QuizDataComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(code);
        packet.WriteInteger(questionIds.Length);

        foreach (var id in questionIds) {
            packet.WriteInteger(id);
        }
    }
}

public sealed class QuizResultsComposer(string code, ImmutableArray<int> wrongQuestionIds) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.QuizResultsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(code);
        packet.WriteInteger(wrongQuestionIds.Length);

        foreach (var id in wrongQuestionIds) {
            packet.WriteInteger(id);
        }
    }
}
