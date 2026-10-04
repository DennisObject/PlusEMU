using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games;

namespace Plus.Communication.Packets.Outgoing.Game;

public class GameListComposer : IServerPacket
{
    private readonly ICollection<GameData> _games;
    public uint MessageId => ServerPacketHeader.GameListComposer;

    public GameListComposer(ICollection<GameData> games)
    {
        _games = games;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_games.Count);
        foreach (var game in _games)
        {
            packet.WriteInteger(game.Id);
            packet.WriteString(game.Name);
            packet.WriteString(game.ColourOne);
            packet.WriteString(game.ColourTwo);
            packet.WriteString(game.ResourcePath);
            packet.WriteString(game.StringThree);
        }
    }
}
