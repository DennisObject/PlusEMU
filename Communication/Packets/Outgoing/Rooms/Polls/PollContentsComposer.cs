using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Polls;

namespace Plus.Communication.Packets.Outgoing.Rooms.Polls;

public sealed class PollContentsComposer(RoomPollSnapshot poll) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.PollContentsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(poll.Id);
        packet.WriteString(poll.Title);
        packet.WriteString(poll.EndMessage);
        packet.WriteInteger(poll.Questions.Length);

        foreach (var question in poll.Questions) {
            WriteQuestion(packet, question);
            packet.WriteInteger(question.Children.Length);

            foreach (var child in question.Children) {
                WriteQuestion(packet, child);
            }
        }

        packet.WriteBoolean(poll.Nps);
    }

    private static void WriteQuestion(IOutgoingPacket packet, PollQuestionSnapshot question)
    {
        packet.WriteInteger(question.Id);
        packet.WriteInteger(question.Order);
        packet.WriteInteger(question.Type);
        packet.WriteString(question.Text);
        packet.WriteInteger(question.Category);
        packet.WriteInteger(question.AnswerType);
        packet.WriteInteger(question.Choices.Length);

        if (question.Type is 1 or 2) {
            foreach (var choice in question.Choices) {
                packet.WriteString(choice.Value);
                packet.WriteString(choice.Label);
                packet.WriteInteger(choice.Category);
            }
        }
    }
}
