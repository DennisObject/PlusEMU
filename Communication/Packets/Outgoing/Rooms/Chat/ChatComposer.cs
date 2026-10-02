using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Chat;

public class ChatComposer : IServerPacket
{
    private readonly int _virtualId;
    private readonly string _message;
    private readonly int _emotion;
    private readonly int _colour;
    public uint MessageId => ServerPacketHeader.ChatComposer;

    public ChatComposer(int virtualId, string message, int emotion, int colour)
    {
        _virtualId = virtualId;
        _message = message;
        _emotion = emotion;
        _colour = colour;
    }

    public void Compose(IOutgoingPacket packet)
    {
        RoomChatPacket.Write(packet, _virtualId, _message, _emotion, _colour);
    }
}