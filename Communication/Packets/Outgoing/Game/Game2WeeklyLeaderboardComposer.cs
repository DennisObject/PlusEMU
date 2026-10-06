using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games;

namespace Plus.Communication.Packets.Outgoing.Game;

public class Game2WeeklyLeaderboardComposer(int gameId, ImmutableArray<WeeklyLeaderboardRow> rows) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2WeeklyLeaderboardComposer;

    public void Compose(IOutgoingPacket packet) => WeeklyLeaderboardWire.Write(packet, gameId, rows);
}
