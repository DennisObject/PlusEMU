using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games;

namespace Plus.Communication.Packets.Outgoing.Game;

public class GameListComposer : IServerPacket
{
    private readonly ImmutableArray<GameListEntry> _games;
    public uint MessageId => ServerPacketHeader.GameListComposer;

    public GameListComposer(ImmutableArray<GameListEntry> games)
    {
        _games = games;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_games.Length);

        foreach (var game in _games) {
            packet.WriteInteger(game.Id);
            packet.WriteString(game.Name);
            packet.WriteString(game.ColourOne);
            packet.WriteString(game.ColourTwo);
            packet.WriteString(game.ResourcePath);
            packet.WriteString(game.StringThree);
        }
    }
}
