using Plus.Communication.Packets.Incoming.Talents;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Talents;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Talents;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class TalentTrackPresentationTests
{
    [Fact]
    public void ComposerWritesConfiguredAchievementIdentityAndActualProgress()
    {
        var habbo = new Habbo();
        habbo.Achievements.TryAdd("ACH_A", new("ACH_A", 0, 3));
        var snapshot = TalentTrackSnapshot.Capture(Levels().Where(level => level.Type == "citizenship"), habbo, Achievements());
        var packet = new HabbiconTestSupport.RecordingPacket();
        new TalentTrackComposer("citizenship", snapshot).Compose(packet);
        Assert.Equal(new object[] {
            "citizenship", 2,
            0, 1, 1, 42, 1, "ACH_A1", 1, 3, 5, 1, "TRADE", 1, "gift", 0,
            1, 0, 1, 42, 2, "ACH_A2", 0, 0, 10, 0, 0
        }, packet.Writes);
    }

    [Fact]
    public void CompletedAchievementLevelsRemainCompletedAfterTheirProgressReset()
    {
        var habbo = new Habbo();
        habbo.Achievements.TryAdd("ACH_A", new("ACH_A", 1, 2));
        var levels = TalentTrackSnapshot.Capture(Levels().Where(level => level.Type == "citizenship"), habbo, Achievements());
        Assert.Equal(2, levels[0].State);
        Assert.Equal(5, levels[0].SubLevels[0].Progress);
        Assert.Equal(1, levels[1].State);
        Assert.Equal(2, levels[1].SubLevels[0].Progress);
    }

    [Fact]
    public void UnknownConfiguredAchievementDoesNotCompleteOrUnlockFollowingLevels()
    {
        var habbo = new Habbo();
        var levels = TalentTrackSnapshot.Capture(new[] {
            new TalentTrackLevel("citizenship", 0, "", "", [new(1, "missing", 0)]),
            new TalentTrackLevel("citizenship", 1, "", "", [])
        }, habbo, Achievements());
        Assert.Equal(1, levels[0].State);
        Assert.Equal(0, levels[1].State);
        Assert.Equal(0, levels[0].SubLevels[0].AchievementId);
    }

    [Fact]
    public void SourceMutationAfterCaptureDoesNotChangeRecomposition()
    {
        var habbo = new Habbo();
        habbo.Achievements.TryAdd("ACH_A", new("ACH_A", 0, 3));
        var levels = Levels();
        var achievements = Achievements();
        var composer = new TalentTrackComposer("citizenship", TalentTrackSnapshot.Capture(levels, habbo, achievements));
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);
        levels[0].Level = 9;
        levels[0].Actions.Clear();
        levels[0].Gifts.Add("late");
        levels[0].GetSubLevels().First().Badge = "CHANGED";
        habbo.Achievements["ACH_A"].Progress = 99;
        achievements["ACH_A"].Id = 99;
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);
        Assert.Equal(before.Writes, after.Writes);
    }

    [Fact]
    public void EmptyLevelsWriteOnlyTheEchoedTypeAndZeroCount()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        new TalentTrackComposer("helper", TalentTrackSnapshot.Capture([], new Habbo(), Achievements())).Compose(packet);
        Assert.Equal(new object[] { "helper", 0 }, packet.Writes);
    }

    [Fact]
    public void EmptyActionsAndGiftsWriteZeroCounts()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        var levels = new[] { new TalentTrackLevel("helper", 4, "", "", []) };
        new TalentTrackComposer("helper", TalentTrackSnapshot.Capture(levels, new Habbo(), Achievements())).Compose(packet);
        Assert.Equal(new object[] { "helper", 1, 4, 2, 0, 0, 0 }, packet.Writes);
    }

    [Fact]
    public async Task GetEventDecodesTheTypeAndDelegatesToThePresentationService()
    {
        var presentation = new RecordingPresentation();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        await new GetTalentTrackEvent(presentation).Parse(client, HabbiconTestSupport.Incoming("citizenship"));
        Assert.Equal(new[] { "citizenship" }, presentation.Types);
        Assert.Empty(sent);
    }

    [Fact]
    public void PresentationFiltersTheRequestedCategoryAndRejectsUnknownCategories()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        var progressed = 0;
        var progression = CatalogSnapshotTestSupport.Proxy<ITalentTrackProgressionService>((method, _) => {
            Assert.Equal("Progress", method);
            progressed++;
            return null;
        });
        var manager = CatalogSnapshotTestSupport.Proxy<IAchievementManager>((method, _) => method == "get_Achievements" ? Achievements() : throw new InvalidOperationException(method));
        var service = new TalentTrackPresentationService(new FixedTalents(Levels()), manager, progression);
        service.ShowLevels(client, "helper");
        service.ShowLevels(client, "invalid");
        var packet = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.TalentTrackComposer, packet.Header);
        var reader = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = packet.Payload };
        Assert.Equal("helper", reader.ReadString());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(1, progressed);
    }

    [Fact]
    public void LevelUpPacketUsesProductVipDaysRatherThanFurnitureSpriteIds()
    {
        var level = TalentTrackSnapshot.Capture(Levels().Take(1), new Habbo(), Achievements())[0];
        var packet = new HabbiconTestSupport.RecordingPacket();
        new TalentLevelUpComposer("citizenship", level).Compose(packet);
        Assert.Equal(new object[] { "citizenship", 0, 1, "TRADE", 1, "gift", 0 }, packet.Writes);
    }

    internal static Dictionary<string, Achievement> Achievements()
    {
        var achievement = new Achievement { Id = 42, GroupName = "ACH_A" };
        achievement.AddLevel(new(1, 0, 0, 5));
        achievement.AddLevel(new(2, 0, 0, 10));
        return new() { ["ACH_A"] = achievement };
    }

    internal static List<TalentTrackLevel> Levels() => [
        new("citizenship", 0, "TRADE", "gift", [new(1, "ACH_A1", 5)]),
        new("citizenship", 1, "", "", [new(1, "ACH_A2", 10)]),
        new("helper", 0, "HELP", "", [new(1, "ACH_A2", 10)])
    ];

    private sealed class RecordingPresentation : ITalentTrackPresentationService
    {
        public List<string> Types { get; } = new();
        public void ShowLevels(GameClient session, string type) => Types.Add(type);
    }

    internal sealed class FixedTalents(List<TalentTrackLevel> levels) : ITalentTrackManager
    {
        public void Init() { }
        public ICollection<TalentTrackLevel> GetLevels() => levels;
    }
}
