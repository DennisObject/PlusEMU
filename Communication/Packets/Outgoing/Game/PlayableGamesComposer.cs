using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Game;

public class PlayableGamesComposer : IServerPacket
{
    private readonly int _gameId;

    public PlayableGamesComposer(int gameId)
    {
        _gameId = gameId;
    }

    public uint MessageId => ServerPacketHeader.PlayableGamesComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_gameId);
        packet.WriteInteger(0);
    }
}
