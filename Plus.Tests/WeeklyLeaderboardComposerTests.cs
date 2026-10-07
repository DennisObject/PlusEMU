using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Game;
using Plus.HabboHotel.Games;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class WeeklyLeaderboardComposerTests
{
    private static readonly ImmutableArray<WeeklyLeaderboardRow> Rows = ImmutableArray.Create(
        new WeeklyLeaderboardRow(1, 50, "Ann", "look1", "f"),
        new WeeklyLeaderboardRow(2, 30, "Bo", "look2", "m"));

    [Fact]
    public void Game3LeaderboardWritesTheExactRowsAndRanks()
    {
        Assert.Equal(new object[]
        {
            2014, 41, 0, 1, 1581,
            2,
            1, 50, 1, "Ann", "look1", "f",
            2, 30, 2, "Bo", "look2", "m",
            0, 3,
        }, Write(new Game3WeeklyLeaderboardComposer(3, Rows)));
    }

    [Fact]
    public void Game3LeaderboardUsesItsOwnMessage()
    {
        var composer = new Game3WeeklyLeaderboardComposer(3, Rows);

        Assert.Equal(Plus.Communication.Packets.Outgoing.ServerPacketHeader.Game3WeeklyLeaderboardComposer, composer.MessageId);
    }

    [Fact]
    public void EmptyLeaderboardWritesZeroRowsAndTheGameId()
    {
        Assert.Equal(new object[] { 2014, 41, 0, 1, 1581, 0, 0, 9 }, Write(new Game3WeeklyLeaderboardComposer(9, ImmutableArray<WeeklyLeaderboardRow>.Empty)));
    }

    [Fact]
    public void CaptureCopiesScalarsAndLowercasesGenderBeforeComposing()
    {
        var habbo = new Habbo { Id = 4, Username = "Cy", Look = "look4", Gender = "M", FastfoodScore = 12 };

        var row = WeeklyLeaderboardRow.Capture(habbo);
        habbo.Username = "Changed";
        habbo.Look = "changed";
        habbo.FastfoodScore = 999;

        Assert.Equal(new WeeklyLeaderboardRow(4, 12, "Cy", "look4", "m"), row);
        Assert.Equal(new object[] { 2014, 41, 0, 1, 1581, 1, 4, 12, 1, "Cy", "look4", "m", 0, 5 }, Write(new Game3WeeklyLeaderboardComposer(5, ImmutableArray.Create(row))));
    }

    [Fact]
    public void RecompositionIsStableAfterSourceListChanges()
    {
        var source = new List<WeeklyLeaderboardRow> { new(1, 50, "Ann", "look1", "f") };
        var composer = new Game3WeeklyLeaderboardComposer(3, source.ToImmutableArray());
        var first = Write(composer);

        source.Clear();
        source.Add(new WeeklyLeaderboardRow(9, 1, "Changed", "x", "m"));

        Assert.Equal(first, Write(composer));
        Assert.Equal(first, Write(composer));
    }

    private static List<object> Write(Plus.Communication.Packets.IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);

        return packet.Writes;
    }
}
