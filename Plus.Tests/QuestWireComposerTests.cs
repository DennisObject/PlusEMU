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
        var quest = new Quest(42, "social", 2, QuestType.SocialChat, 5, "talk", 10, "bit", 3, 6, 0);
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
}
