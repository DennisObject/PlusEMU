using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Game;

/// <summary>AIR Game2AccountGameStatus: free games left (-1 = unlimited) and games played in total.</summary>
public class GameAccountStatusComposer(int gameId, int freeGamesLeft = -1, int gamesPlayed = 0) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.GameAccountStatusComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(gameId);
        packet.WriteInteger(freeGamesLeft);
        packet.WriteInteger(gamesPlayed);
    }
}
