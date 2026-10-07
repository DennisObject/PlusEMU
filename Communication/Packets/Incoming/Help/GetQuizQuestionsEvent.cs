using System.Buffers.Binary;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Help;

namespace Plus.Communication.Packets.Incoming.Help;

internal sealed class GetQuizQuestionsEvent(ISafetyQuizService quizzes) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var code = QuizPacket.ReadCode(packet);

        if (code != null && !packet.HasDataRemaining()) {
            quizzes.Start(session, code);
        }

        return Task.CompletedTask;
    }
}

internal static class QuizPacket
{
    public static string? ReadCode(IIncomingPacket packet)
    {
        if (packet.Buffer.Length < sizeof(ushort)) {
            return null;
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(packet.Buffer.Span);

        return length is >= 1 and <= 32 && packet.Buffer.Length >= length + sizeof(ushort) ? packet.ReadString() : null;
    }
}
