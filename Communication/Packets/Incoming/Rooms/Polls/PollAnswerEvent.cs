using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Polls;

namespace Plus.Communication.Packets.Incoming.Rooms.Polls;

internal sealed class PollAnswerEvent(IRoomPollService polls, IRoomWordQuizService quizzes) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var pollId = packet.ReadInt();
        var questionId = packet.ReadInt();
        var count = packet.ReadInt();
        if (count is < 0 or > 64) {
            return Task.CompletedTask;
        }

        var answers = new string[count];
        for (var i = 0; i < count; i++) {
            answers[i] = packet.ReadString();
        }

        if (pollId == 0) {
            quizzes.Answer(session, pollId, questionId, answers);
        }
        else {
            polls.Answer(session, pollId, questionId, answers);
        }
        return Task.CompletedTask;
    }
}
