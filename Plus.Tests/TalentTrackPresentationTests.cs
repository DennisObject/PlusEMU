using System.Collections.Immutable;
using Plus.Communication.Packets.Incoming.Talents;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Talents;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Talents;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class TalentTrackPresentationTests
{
    [Fact]
    public void ComposerWritesEveryCategoryLevelSubLevelActionAndGiftInOrder()
    {
        var snapshot = TalentTrackSnapshot.Capture(Levels());
        var packet = new HabbiconTestSupport.RecordingPacket();

        new TalentTrackComposer("citizenship", snapshot).Compose(packet);

        Assert.Equal(new object[]
        {
            "citizenship", 2,
            1, 0, 2,
            0, 0, "ACH_A1", 0, 0, 60,
            0, 0, "ACH_A2", 0, 0, 120,
            2, "a1", "a2",
            1, "g1", 0,
            2, 0, 1,
            0, 0, "ACH_H1", 0, 0, 10,
            1, "h1",
            1, "", 0,
        }, packet.Writes);
    }

    [Fact]
    public void SourceMutationAfterCaptureDoesNotChangeRecomposition()
    {
        var levels = Levels();
        var snapshot = TalentTrackSnapshot.Capture(levels);
        var composer = new TalentTrackComposer("citizenship", snapshot);
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);

        levels[0].Level = 9;
        levels[0].Actions.Clear();
        levels[0].Gifts.Add("late");
        levels[0].GetSubLevels().First().Badge = "CHANGED";
        levels[0].GetSubLevels().First().RequiredProgress = 999;
        levels.Add(new TalentTrackLevel("late", 3, "x", "y", []));
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);

        Assert.Equal(before.Writes, after.Writes);
        Assert.Equal(new object[] { "citizenship", 2, 1, 0, 2 }, before.Writes.Take(5));
    }

    [Fact]
    public void EmptyLevelsWriteOnlyTheEchoedTypeAndZeroCount()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();

        new TalentTrackComposer("helper", TalentTrackSnapshot.Capture(Array.Empty<TalentTrackLevel>())).Compose(packet);

        Assert.Equal(new object[] { "helper", 0 }, packet.Writes);
    }

    [Fact]
    public void LevelWithoutSubLevelsOrGiftsWritesZeroCounts()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        var levels = new[] { new TalentTrackLevel("helper", 4, "", "", []) };

        new TalentTrackComposer("helper", TalentTrackSnapshot.Capture(levels)).Compose(packet);

        Assert.Equal(new object[] { "helper", 1, 4, 0, 0, 1, "", 1, "", 0 }, packet.Writes);
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
    public void PresentationServiceSendsOneComposerOfTheCapturedLevels()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });

        new TalentTrackPresentationService(new FixedTalents(Levels())).ShowLevels(client, "citizenship");

        var packet = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.TalentTrackComposer, packet.Header);
        var reader = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = packet.Payload };
        Assert.Equal("citizenship", reader.ReadString());
        Assert.Equal(2, reader.ReadInt());
    }

    private static List<TalentTrackLevel> Levels() =>
    [
        new("citizenship", 1, "a1|a2", "g1", [new(1, "ACH_A1", 60), new(2, "ACH_A2", 120)]),
        new("helper", 2, "h1", "", [new(1, "ACH_H1", 10)]),
    ];

    private sealed class RecordingPresentation : ITalentTrackPresentationService
    {
        public List<string> Types { get; } = new();
        public void ShowLevels(GameClient session, string type) => Types.Add(type);
    }

    private sealed class FixedTalents(List<TalentTrackLevel> levels) : ITalentTrackManager
    {
        public void Init()
        {
        }
        public ICollection<TalentTrackLevel> GetLevels() => levels;
    }
}
