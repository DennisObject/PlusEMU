using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Chat;

public class WhisperComposer : IServerPacket
{
    private readonly int _virtualId;
    private readonly string _text;
    private readonly int _emotion;
    private readonly int _colour;

    public uint MessageId => ServerPacketHeader.WhisperComposer;

    public WhisperComposer(int virtualId, string text, int emotion, int colour)
    {
        _virtualId = virtualId;
        _text = text;
        _emotion = emotion;
        _colour = colour;
    }

    public void Compose(IOutgoingPacket packet)
    {
        RoomChatPacket.Write(packet, _virtualId, _text, _emotion, _colour);
    }
}
