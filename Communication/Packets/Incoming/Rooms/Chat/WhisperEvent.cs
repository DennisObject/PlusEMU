using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Chat;

namespace Plus.Communication.Packets.Incoming.Rooms.Chat;

public sealed class WhisperEvent(IRoomChatService chat) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) =>
        chat.Whisper(session, packet.ReadString(), packet.ReadInt());
}
