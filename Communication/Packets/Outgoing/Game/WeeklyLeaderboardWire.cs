using System.Collections.Immutable;
using Plus.HabboHotel.Games;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Game;

internal static class WeeklyLeaderboardWire
{
    public static void Write(IOutgoingPacket packet, int gameId, ImmutableArray<WeeklyLeaderboardRow> rows)
    {
        packet.WriteInteger(2014);
        packet.WriteInteger(41);
        packet.WriteInteger(0);
        packet.WriteInteger(1);
        packet.WriteInteger(1581);

        //Used to generate the ranking numbers.
        packet.WriteInteger(rows.Length); //Count

        for (var index = 0; index < rows.Length; index++) {
            var row = rows[index];
            packet.WriteInteger(row.UserId); //Id
            packet.WriteInteger(row.Score); //Score
            packet.WriteInteger(index + 1); //Rank
            packet.WriteString(row.Username); //Username
            packet.WriteString(row.Look); //Figure
            packet.WriteString(row.Gender); //Gender
        }

        packet.WriteInteger(0); //
        packet.WriteInteger(gameId); //Game Id?
    }
}
