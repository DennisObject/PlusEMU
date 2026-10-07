using Plus.Communication.Packets.Outgoing.Game.SnowStorm;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Outgoing.Game;

/// <summary>AIR Game2WeeklyLeaderboard: week header, then the entries.</summary>
public class Game2WeeklyLeaderboardComposer(SnowStormLeaderboardPage page) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2WeeklyLeaderboardComposer;

    public void Compose(IOutgoingPacket packet) => SnowStormLeaderboardWire.WriteWeekly(packet, page);
}
