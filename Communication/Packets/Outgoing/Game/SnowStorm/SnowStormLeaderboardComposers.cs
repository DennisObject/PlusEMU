using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Outgoing.Game.SnowStorm;

internal static class SnowStormLeaderboardWire
{
    public static void WriteBase(IOutgoingPacket packet, SnowStormLeaderboardPage page)
    {
        packet.WriteInteger(page.Entries.Length);

        foreach (var entry in page.Entries) {
            packet.WriteInteger(entry.UserId);
            packet.WriteInteger(entry.Score);
            packet.WriteInteger(entry.Rank);
            packet.WriteString(entry.Name);
            packet.WriteString(entry.Figure);
            packet.WriteString(entry.Gender);
        }

        packet.WriteInteger(page.TotalListSize);
        packet.WriteInteger(page.GameTypeId);
    }

    public static void WriteWeekly(IOutgoingPacket packet, SnowStormLeaderboardPage page)
    {
        var week = page.Week ?? new SnowStormLeaderboardWeek(0, 0, 0, 0, 0);
        packet.WriteInteger(week.Year);
        packet.WriteInteger(week.Week);
        packet.WriteInteger(week.MaxOffset);
        packet.WriteInteger(week.CurrentOffset);
        packet.WriteInteger(week.MinutesUntilReset);
        WriteBase(packet, page);
    }
}

public sealed class Game2TotalLeaderboardComposer(SnowStormLeaderboardPage page) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2TotalLeaderboardComposer;

    public void Compose(IOutgoingPacket packet) => SnowStormLeaderboardWire.WriteBase(packet, page);
}

public sealed class Game2FriendsLeaderboardComposer(SnowStormLeaderboardPage page) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2FriendsLeaderboardComposer;

    public void Compose(IOutgoingPacket packet) => SnowStormLeaderboardWire.WriteBase(packet, page);
}

public sealed class Game2WeeklyFriendsLeaderboardComposer(SnowStormLeaderboardPage page) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2WeeklyFriendsLeaderboardComposer;

    public void Compose(IOutgoingPacket packet) => SnowStormLeaderboardWire.WriteWeekly(packet, page);
}

public sealed class Game2TotalGroupLeaderboardComposer(SnowStormLeaderboardPage page) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2TotalGroupLeaderboardComposer;

    public void Compose(IOutgoingPacket packet)
    {
        SnowStormLeaderboardWire.WriteBase(packet, page);
        packet.WriteInteger(page.FavouriteGroupId);
    }
}

public sealed class Game2WeeklyGroupLeaderboardComposer(SnowStormLeaderboardPage page) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.Game2WeeklyGroupLeaderboardComposer;

    public void Compose(IOutgoingPacket packet)
    {
        SnowStormLeaderboardWire.WriteWeekly(packet, page);
        packet.WriteInteger(page.FavouriteGroupId);
    }
}

public sealed class SnowWarGameTokensComposer(ImmutableArray<SnowStormTokenOffer> offers) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.SnowWarGameTokensComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(offers.Length);

        foreach (var offer in offers) {
            packet.WriteInteger(offer.Id);
            packet.WriteString(offer.LocalizationId);
            packet.WriteInteger(offer.PriceCredits);
            packet.WriteInteger(offer.PricePoints);
            packet.WriteInteger(offer.PointsType);
        }
    }
}
