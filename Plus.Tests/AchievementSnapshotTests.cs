using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Game.Lobby;
using Plus.Communication.Packets.Incoming.Inventory.Achievements;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Game;
using Plus.Communication.Packets.Outgoing.Inventory.Achievements;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class AchievementSnapshotTests
{
    // SHA-256 of the pre-migration achievement composer payloads for the scenarios in BaselineLines.
    private const string BaselineSha256 = "24cf9f92a0586099715752238407ed531cf5284e16f69dde0912cc0320e25a4f";

    private static readonly AchievementSnapshotService Service = new();

    [Fact]
    public void BadgeDefinitionsFreezeNamesSparseLevelsAndEmptyEntries()
    {
        var defined = Achievement("ACH_SOCIAL", "social", 0, (4, 40), (1, 10));
        var empty = Achievement("ACH_EMPTY", "social", 0);
        var source = new List<Achievement> { defined, empty };
        var composer = new BadgeDefinitionsComposer(Service.CaptureDefinitions(source));
        var expected = new List<object> { 2, "SOCIAL", 2, 4, 40, 1, 10, "EMPTY", 0 };

        Assert.Equal(expected, Writes(composer));
        defined.GroupName = "ACH_CHANGED";
        defined.Levels.Clear();
        empty.Levels.Add(9, new AchievementLevel(9, 0, 0, 99));
        source.Clear();

        Assert.Equal(expected, Writes(composer));
        Assert.Equal(expected, Writes(composer));
    }

    [Fact]
    public void ComposedPayloadsMatchThePreMigrationBaseline()
    {
        var lines = BaselineLines();

        Assert.Equal(BaselineSha256, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(lines.Select(line => line + "\n"))))));
    }

    [Fact]
    public void SparseLevelsResolveToTheNearestDefinedLevel()
    {
        var achievement = Achievement("ACH_SPARSE", "social", 0, (1, 10), (3, 20), (5, 30));
        var snapshot = Assert.Single(Service.Capture(UserWith(level: 1, progress: 2, "ACH_SPARSE"), [achievement]));

        Assert.Equal(3, snapshot.TargetLevel);
        Assert.Equal("ACH_SPARSE3", snapshot.Badge);
        Assert.Equal(20, snapshot.Requirement);
    }

    [Fact]
    public void CompletedGameAchievementStaysOnItsLastLevelInsteadOfRunningPastIt()
    {
        var achievement = Achievement("GAME_DONE", "games", 9, (1, 5), (2, 10));
        var snapshots = Service.Capture(UserWith(level: 2, progress: 10, "GAME_DONE"), [achievement]);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal(2, snapshot.TargetLevel);
        Assert.True(snapshot.Completed);
        Assert.Equal(new List<object> { 9, 1 }, Writes(new GameAchievementListComposer(9, snapshots)).Take(2).ToList());
    }

    [Fact]
    public void SparseLevelsAreNotCompletedUntilTheHighestDefinedLevel()
    {
        // Levels 1 and 4 only: holding level 2 is not complete, while the Levels.Count of 2 would have said so.
        var achievement = Achievement("ACH_GAPS", "social", 0, (1, 10), (4, 40));

        var midway = Assert.Single(Service.Capture(UserWith(level: 2, progress: 5, "ACH_GAPS"), [achievement]));
        var finished = Assert.Single(Service.Capture(UserWith(level: 4, progress: 40, "ACH_GAPS"), [achievement]));

        Assert.Equal(4, midway.TargetLevel);
        Assert.False(midway.Completed);
        Assert.True(finished.Completed);
        Assert.Equal(2, midway.TotalLevels);
    }

    [Fact]
    public void NullGroupNameAndCategoryFallBackToEmptyTextWithoutThrowing()
    {
        var achievement = Achievement("ACH_NULLS", "social", 0, (1, 5));
        achievement.GroupName = null;
        achievement.Category = null;

        var snapshot = Assert.Single(Service.Capture(new Habbo { Id = 5, Username = "nulls" }, [achievement]));

        Assert.Equal("", snapshot.Category);
        Assert.Equal("1", snapshot.Badge);
        Assert.Equal(1, snapshot.TargetLevel);
        Assert.False(snapshot.Completed);
    }

    [Fact]
    public void AchievementWithoutLevelsIsLeftOutOfTheList()
    {
        var empty = Achievement("ACH_EMPTY", "social", 0);

        var snapshots = Service.Capture(UserWith(level: 0, progress: 0), [empty]);

        Assert.Empty(snapshots);
        Assert.Equal(new List<object> { 0, "" }, Writes(new AchievementsComposer(snapshots)));
    }

    [Fact]
    public void NullProgressStartsAtTheFirstLevelWithZeroProgress()
    {
        var snapshot = Assert.Single(Service.Capture(new Habbo { Id = 5, Username = "new" }, [Achievement("ACH_NEW", "social", 0, (1, 5), (2, 10))]));

        Assert.Equal(1, snapshot.TargetLevel);
        Assert.Equal(0, snapshot.Progress);
        Assert.False(snapshot.Completed);
    }

    [Fact]
    public void CapturedSnapshotDoesNotFollowLaterSourceMutation()
    {
        var achievement = Achievement("ACH_MUTABLE", "social", 0, (1, 5), (2, 10));
        var snapshots = Service.Capture(UserWith(level: 0, progress: 3), [achievement]);
        var before = Writes(new AchievementsComposer(snapshots));

        achievement.GroupName = "CHANGED";
        achievement.AddLevel(new AchievementLevel(3, 30, 3, 40));
        achievement.Category = "changed";

        Assert.Equal(before, Writes(new AchievementsComposer(snapshots)));
        Assert.Equal(2, snapshots[0].TotalLevels);
        Assert.Equal(new List<object> { 0, "" }, Writes(new AchievementsComposer(ImmutableArray<AchievementProgressSnapshot>.Empty)));
    }

    [Fact]
    public void RecomposingTheSameSnapshotIsStable()
    {
        var snapshots = Service.Capture(UserWith(level: 1, progress: 4), [Achievement("ACH_SOCIAL", "social", 0, (1, 5), (2, 10), (3, 20))]);

        Assert.Equal(Writes(new AchievementsComposer(snapshots)), Writes(new AchievementsComposer(snapshots)));
    }

    [Fact]
    public async Task GetAchievementsEventSendsOnlyTheSnapshotComposer()
    {
        var achievement = Achievement("ACH_HANDLER", "social", 0, (1, 5), (2, 10));
        var manager = Proxy<IAchievementManager>((method, _) => method == "get_Achievements"
            ? new Dictionary<string, Achievement> { ["ACH_HANDLER"] = achievement }
            : throw new InvalidOperationException(method));
        var (client, sent) = HabbiconTestSupport.Client(UserWith(level: 0, progress: 1, "ACH_HANDLER"));

        await new GetAchievementsEvent(new AchievementShowcaseService(manager, Service)).Parse(client, Packet());

        var message = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.AchievementsComposer, message.Header);
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(message.Payload));
    }

    [Fact]
    public async Task GetGameAchievementsEventDelegatesTheGameListToTheSnapshotService()
    {
        var game = Achievement("GAME_HANDLER", "games", 9, (1, 5), (2, 10));
        var manager = Proxy<IAchievementManager>((method, args) => method == "GetGameAchievements" && (int)args[0]! == 9
            ? new List<Achievement> { game }
            : throw new InvalidOperationException(method));
        var (client, sent) = HabbiconTestSupport.Client(UserWith(level: 2, progress: 10, "GAME_HANDLER"));

        await new GetGameAchievementsEvent(new AchievementShowcaseService(manager, Service)).Parse(client, Packet(9));

        Assert.Equal(new[] { ServerPacketHeader.GameAccountStatusComposer, ServerPacketHeader.PlayableGamesComposer, ServerPacketHeader.GameAchievementListComposer },
            sent.Select(message => message.Header));
        var list = sent[2].Payload;
        Assert.Equal(9, BinaryPrimitives.ReadInt32BigEndian(list));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(list.AsSpan(4)));
    }

    private static IEnumerable<string> BaselineLines()
    {
        var lines = new List<string>();
        void Add(string name, Habbo habbo, Func<Habbo, ImmutableArray<AchievementProgressSnapshot>> capture, Func<ImmutableArray<AchievementProgressSnapshot>, IServerPacket> compose)
        {
            lines.Add($"{name}: {string.Join("|", Writes(compose(capture(habbo))).Select(write => $"{write.GetType().Name}:{write}"))}");
        }
        var social = Achievement("ACH_SOCIAL", "social", 0, (1, 5), (2, 10), (3, 20));
        var game = Achievement("GAME_X", "games", 9, (1, 5), (2, 10), (3, 20));
        Add("none", new Habbo { Id = 7, Username = "u" }, habbo => Service.Capture(habbo, [social]), snapshots => new AchievementsComposer(snapshots));
        Add("partial", UserWith(level: 1, progress: 7), habbo => Service.Capture(habbo, [social]), snapshots => new AchievementsComposer(snapshots));
        Add("game-partial", UserWith(level: 1, progress: 4, "GAME_X"), habbo => Service.Capture(habbo, [game]), snapshots => new GameAchievementListComposer(9, snapshots));
        Add("completed", UserWith(level: 3, progress: 30), habbo => Service.Capture(habbo, [social]), snapshots => new AchievementsComposer(snapshots));
        Add("game-none", new Habbo { Id = 9, Username = "w" }, habbo => Service.Capture(habbo, [game]), snapshots => new GameAchievementListComposer(9, snapshots));
        return lines;
    }

    // Levels are (level, requirement); reward pixels follow the level number, as the fixtures always did.
    private static Achievement Achievement(string group, string category, int gameId, params (int Level, int Requirement)[] levels)
    {
        var achievement = new Achievement { Id = 1, GroupName = group, Category = category, GameId = gameId };
        foreach (var (level, requirement) in levels)
            achievement.AddLevel(new AchievementLevel(level, level * 10, level, requirement));
        return achievement;
    }

    private static Habbo UserWith(int level, int progress, string group = "ACH_SOCIAL")
    {
        var habbo = new Habbo { Id = 7, Username = "u" };
        habbo.Achievements.TryAdd(group, new UserAchievement(group, level, progress));
        return habbo;
    }

    private static List<object> Writes(IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);
        return packet.Writes;
    }

    private static FlashIncomingPacket Packet(params int[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            stream.Write(bytes);
        }
        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class => CatalogSnapshotTestSupport.Proxy<T>(call);
}
