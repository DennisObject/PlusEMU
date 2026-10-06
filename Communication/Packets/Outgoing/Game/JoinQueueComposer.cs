using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Game;

public class JoinQueueComposer : IServerPacket
{
    private readonly int _gameId;
    public uint MessageId => ServerPacketHeader.JoinQueueComposer;

    public JoinQueueComposer(int gameId)
    {
        _gameId = gameId;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(_gameId);
}
