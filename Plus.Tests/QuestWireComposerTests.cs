using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Quests;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class QuestWireComposerTests
{
    [Fact]
    public void StartedComposerWritesExactPreparedFields()
    {
        QuestWireData data = new("social", 2, 7, 3, 42, true, "CHAT", "bit", 10, "talk", 4, 5, 6);
        var packet = new HabbiconTestSupport.RecordingPacket();

        new QuestStartedComposer(data).Compose(packet);

        Assert.Equal(new object[] { "social", 2, 7, 3, 42, true, "CHAT", "bit", 10, "talk", 4, 5, 6, "", "", true }, packet.Writes);
    }

    [Fact]
    public void CompletedComposerPreservesTrailingActivationFlag()
    {
        QuestWireData data = new("social", 2, 7, 3, 42, false, "CHAT", "bit", 10, "talk", 5, 5, 6);
        var packet = new HabbiconTestSupport.RecordingPacket();

        new QuestCompletedComposer(data).Compose(packet);

        Assert.Equal(17, packet.Writes.Count);
        Assert.Equal(true, packet.Writes[^1]);
    }

    [Fact]
    public void PreparedSnapshotRecomposesIdenticallyAfterSessionMutation()
    {
        var stats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 42, 0, 0, "", 0);
        var habbo = new Habbo { Id = 7, HabboStats = stats, Quests = new() { [42] = 4 } };
        var (client, _) = HabbiconTestSupport.Client(habbo);
        var quest = new Quest(42, "social", 2, QuestType.SocialChat, 5, "talk", 10, "bit", 3,
            DateTimeOffset.FromUnixTimeSeconds(6), null);
        var composer = new QuestStartedComposer(QuestWireDataFactory.Create(client, quest, 7));
        var first = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(first);

        habbo.HabboStats.QuestId = 0;
        habbo.Quests[42] = 99;
        var second = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(second);

        Assert.Equal(first.Writes, second.Writes);
    }

    [Fact]
    public void ListComposerUsesFrozenOrderingAndCount()
    {
        var active = new QuestWireData("a", 0, 1, 3, 1, true, "A", "", 1, "a", 0, 1, 0);
        var dead = QuestWireDataFactory.Empty("b");
        var source = ImmutableArray.Create(active, dead);
        var packet = new HabbiconTestSupport.RecordingPacket();

        new QuestListComposer(new(true, source)).Compose(packet);

        Assert.Equal(2, packet.Writes[0]);
        Assert.Equal("a", packet.Writes[1]);
        Assert.Equal("b", packet.Writes[17]);
        Assert.Equal(true, packet.Writes[^1]);
    }

    [Fact]
    public void UnlockTimestampUsesLegacyNullFloorAndRangeClampAtSnapshotBoundary()
    {
        var stats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 42, 0, 0, "", 0);
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 7, HabboStats = stats });

        Assert.Equal(0, QuestWireDataFactory.Create(client, QuestWithUnlock(null), 1).TimeUnlock);
        Assert.Equal(0, QuestWireDataFactory.Create(client,
            QuestWithUnlock(DateTimeOffset.UnixEpoch.AddSeconds(-1)), 1).TimeUnlock);
        Assert.Equal(1_700_000_000, QuestWireDataFactory.Create(client,
            QuestWithUnlock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).AddTicks(9_999_999)), 1).TimeUnlock);
        Assert.Equal(int.MaxValue, QuestWireDataFactory.Create(client,
            QuestWithUnlock(DateTimeOffset.FromUnixTimeSeconds((long)int.MaxValue + 1)), 1).TimeUnlock);
    }

    [Fact]
    public void QuestEndBoundaryIsExplicitAndNullSafe()
    {
        var deadline = new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.Zero);
        Assert.False(QuestWithLock(null).IsEndedAt(deadline));
        Assert.False(QuestWithLock(deadline).IsEndedAt(deadline.AddTicks(-1)));
        Assert.True(QuestWithLock(deadline).IsEndedAt(deadline));
        Assert.True(QuestWithLock(deadline).IsEndedAt(deadline.AddTicks(1)));
    }

    private static Quest QuestWithUnlock(DateTimeOffset? unlocksAt) =>
        new(42, "social", 1, QuestType.SocialChat, 1, "talk", 10, "", 3, unlocksAt, null);

    private static Quest QuestWithLock(DateTimeOffset? locksAt) =>
        new(42, "social", 1, QuestType.SocialChat, 1, "talk", 10, "", 3, null, locksAt);
}
