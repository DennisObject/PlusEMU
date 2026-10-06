using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Game;

// TODO @80O: Implement
public class GameAccountStatusComposer : IServerPacket
{
    private readonly int _gameId;
    public uint MessageId => ServerPacketHeader.GameAccountStatusComposer;

    public GameAccountStatusComposer(int gameId)
    {
        _gameId = gameId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_gameId);
        packet.WriteInteger(-1); // Games Left
        packet.WriteInteger(0); //Was 16?
    }
}
