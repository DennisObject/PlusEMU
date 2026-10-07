using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Help;

namespace Plus.Communication.Packets.Incoming.Help
{
    internal sealed class PostQuizAnswersEvent(ISafetyQuizService quizzes) : IPacketEvent
    {
        public Task Parse(GameClient session, IIncomingPacket packet)
        {
            var code = QuizPacket.ReadCode(packet);

            if (code == null || packet.Buffer.Length < sizeof(int)) {
                return Task.CompletedTask;
            }

            var count = packet.ReadInt();

            if (count is < 1 or > 64 || packet.Buffer.Length != count * sizeof(int)) {
                return Task.CompletedTask;
            }

            var answers = new int[count];

            for (var index = 0; index < count; index++) {
                answers[index] = packet.ReadInt();
            }

            if (!packet.HasDataRemaining()) {
                quizzes.Submit(session, code, answers);
            }

            return Task.CompletedTask;
        }
    }
}
