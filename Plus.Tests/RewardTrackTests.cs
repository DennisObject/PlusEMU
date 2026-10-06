using System.Text.Json;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Quests;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Xunit;

namespace Plus.Tests;

public class RewardTrackTests
{
    [Fact]
    public void ChatProgressPaysCrossedLevelsAndBoostsPremium()
    {
        var track = ChatTrack();
        var task = track.Tasks[0];

        var plain = new UserRewardTrackState(track.Id, 0, false);
        var five = RewardTrackRules.Advance(track, plain, task, 5);
        Assert.Equal(10, five.PointsGranted);
        Assert.Equal(5, five.Count);
        Assert.Equal(10, plain.Points);

        var premium = new UserRewardTrackState(track.Id, 0, true);
        var boosted = RewardTrackRules.Advance(track, premium, task, 5);
        Assert.Equal(15, boosted.PointsGranted);

        var full = new UserRewardTrackState(track.Id, 0, false);
        var capped = RewardTrackRules.Advance(track, full, task, 100);
        Assert.Equal(70, capped.PointsGranted);
        Assert.Equal(100, capped.Count);

        var boostedFull = new UserRewardTrackState(track.Id, 0, true);
        var boostedCap = RewardTrackRules.Advance(track, boostedFull, task, 100);
        Assert.Equal(105, boostedCap.PointsGranted);
        Assert.Equal(105, boostedFull.Points);
    }

    [Fact]
    public void ASecondClimbOverTheSamePeakPaysNothing()
    {
        var track = ChatTrack();
        var task = track.Tasks[0];
        var state = new UserRewardTrackState(track.Id, 0, false);
        var first = RewardTrackRules.Advance(track, state, task, 5);
        Assert.Equal(10, first.PointsGranted);
        state.SetStoredCount(task.Id, 0);

        var second = RewardTrackRules.Advance(track, state, task, 5);

        Assert.Equal(0, second.PointsGranted);
        Assert.Equal(10, state.Points);
        Assert.Equal(5, state.ProgressOf(task.Id));
        Assert.Equal(5, state.PeakOf(task.Id));
    }

    [Fact]
    public void ProgressStopsAtTheLastLevel()
    {
        var track = ChatTrack();
        var task = track.Tasks[0];
        var state = new UserRewardTrackState(track.Id, 0, false);
        var step = RewardTrackRules.Advance(track, state, task, 100);
        Assert.Equal(100, step.Count);
        Assert.Equal(70, state.Points);

        var again = RewardTrackRules.Advance(track, state, task, 50);

        Assert.False(again.Changed);
        Assert.Equal(100, state.ProgressOf(task.Id));
        Assert.Equal(70, state.Points);
    }

    [Fact]
    public void ClaimRejectsMissingPointsPremiumLockRepeatsAndUnknownPrizes()
    {
        var track = ChatTrack();
        var broke = new UserRewardTrackState(track.Id, 0, false);
        Assert.Equal(RewardTrackResults.NotEnoughPoints, RewardTrackRules.PreviewClaim(track, broke, "track_champ"));

        var locked = new UserRewardTrackState(track.Id, 200, false);
        Assert.Equal(RewardTrackResults.PremiumRequired, RewardTrackRules.PreviewClaim(track, locked, "track_champ_premium"));

        var ready = new UserRewardTrackState(track.Id, 50, false);
        Assert.Equal(RewardTrackResults.Ok, RewardTrackRules.PreviewClaim(track, ready, "track_champ"));
        ready.MarkClaimed("track_champ");
        Assert.Equal(RewardTrackResults.AlreadyClaimed, RewardTrackRules.PreviewClaim(track, ready, "track_champ"));
        Assert.Equal(RewardTrackResults.Unknown, RewardTrackRules.PreviewClaim(track, ready, "missing"));

        track.AddPrize(new RewardTrackPrize("furni", 1, 1, "furni", "", 1, false, 3));
        var rich = new UserRewardTrackState(track.Id, 50, false);
        Assert.Equal(RewardTrackResults.Unknown, RewardTrackRules.PreviewClaim(track, rich, "furni"));
    }

    [Fact]
    public void PremiumPurchaseChecksCreditsThenAppliesOnce()
    {
        var track = ChatTrack();
        var state = new UserRewardTrackState(track.Id, 0, false);
        var poor = RewardTrackRules.Quote(track, state, 24, 0);
        Assert.Equal(RewardTrackResults.NotEnoughCurrency, poor.Result);
        Assert.Equal(24, poor.Credits);
        Assert.False(state.Premium);
        Assert.Equal(0, state.Points);

        var bought = RewardTrackRules.Quote(track, state, 25, 0);
        Assert.Equal(RewardTrackResults.Ok, bought.Result);
        Assert.Equal(0, bought.Credits);
        Assert.Equal(0, bought.Diamonds);
        Assert.Equal(25, bought.Points);
        state.ApplyPremium(bought.Points);
        Assert.True(state.Premium);
        Assert.Equal(25, state.Points);

        var again = RewardTrackRules.Quote(track, state, 100, 0);
        Assert.Equal(RewardTrackResults.AlreadyPremium, again.Result);
        Assert.True(state.Premium);
        Assert.Equal(25, state.Points);
    }

    [Fact]
    public void HeadersMatchTheNitroClientNumbers()
    {
        Assert.Equal(48000u, ClientPacketHeader.GetRewardTracksEvent);
        Assert.Equal(48001u, ClientPacketHeader.ClaimRewardTrackPrizeEvent);
        Assert.Equal(48002u, ClientPacketHeader.PurchaseRewardTrackPremiumEvent);
        Assert.Equal(48003u, ServerPacketHeader.RewardTracksComposer);
        Assert.Equal(48004u, ServerPacketHeader.RewardTrackClaimResultComposer);
        Assert.Equal(48005u, ServerPacketHeader.RewardTrackProgressComposer);
        Assert.Equal(48006u, ServerPacketHeader.RewardTrackPremiumPurchaseResultComposer);
        Assert.Equal(2248u, ServerPacketHeader.UserChangeComposer);
        Assert.NotEqual(ServerPacketHeader.UserChangeComposer, ServerPacketHeader.RewardTrackPremiumPurchaseResultComposer);

        AssertClient("3.6.0.json", 9450, 1111, 3022, 2327, 9451, 9452, 2248);
        AssertClient("1.6.6.json", 0, 0, 0, 0, 0, 0, 0);
        AssertClient("OCTANE-3-6-0-FLOOR-20260909.json", 9450, 1111, 3022, 2327, 9451, 9452, 2248);
    }

    [Fact]
    public void TrackPacketMatchesTheClientFieldOrder()
    {
        var track = ChatTrack();
        var state = new UserRewardTrackState(track.Id, 10, false);
        state.SetStoredCount("chat_with_users", 5);
        var packet = new RecordingPacket();
        new RewardTracksComposer(false, [RewardTrackWireSnapshot.Capture(track, state)], false).Compose(packet);

        Assert.Equal(new object[]
        {
            false, 1,
            "introduction", "blue", 10, true, 1.5, 25, 0, 25, false, false, false,
            1,
            "chat_with_users", "chat_with_someone", "", 5, false,
            3,
            5, 10, false,
            25, 20, false,
            100, 40, false,
            2,
            "track_champ", 50, (short)4, "badge", "ACH_RewardTracksCompleted1", 1, false, false, false,
            "track_champ_premium", 200, (short)4, "badge", "ACH_RewardTracksCompleted2", 1, true, false, false,
            false
        }, packet.Writes);

        var progress = new RecordingPacket();
        new RewardTrackProgressComposer("introduction", "chat_with_users", 5, 10).Compose(progress);
        Assert.Equal(new object[] { "introduction", "chat_with_users", 5, 10 }, progress.Writes);
        var claim = new RecordingPacket();
        new RewardTrackClaimResultComposer("introduction", "track_champ", RewardTrackResults.NotEnoughPoints).Compose(claim);
        Assert.Equal(new object[] { "introduction", "track_champ", 2 }, claim.Writes);
        var premium = new RecordingPacket();
        new RewardTrackPremiumPurchaseResultComposer("introduction", RewardTrackResults.NotEnoughCurrency, 0).Compose(premium);
        Assert.Equal(new object[] { "introduction", 5, 0 }, premium.Writes);
    }

    private static void AssertClient(string file, uint get, uint claim, uint purchase, uint tracks, uint claimResult, uint progress, uint premiumResult)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RevisionPath(file)));
        var incoming = document.RootElement.GetProperty("IncomingHeaders");
        var outgoing = document.RootElement.GetProperty("OutgoingHeaders");
        Assert.Equal(get, incoming.GetProperty(nameof(ClientPacketHeader.GetRewardTracksEvent)).GetUInt32());
        Assert.Equal(claim, incoming.GetProperty(nameof(ClientPacketHeader.ClaimRewardTrackPrizeEvent)).GetUInt32());
        Assert.Equal(purchase, incoming.GetProperty(nameof(ClientPacketHeader.PurchaseRewardTrackPremiumEvent)).GetUInt32());
        Assert.Equal(tracks, outgoing.GetProperty(nameof(ServerPacketHeader.RewardTracksComposer)).GetUInt32());
        Assert.Equal(claimResult, outgoing.GetProperty(nameof(ServerPacketHeader.RewardTrackClaimResultComposer)).GetUInt32());
        Assert.Equal(progress, outgoing.GetProperty(nameof(ServerPacketHeader.RewardTrackProgressComposer)).GetUInt32());
        Assert.Equal(premiumResult, outgoing.GetProperty(nameof(ServerPacketHeader.RewardTrackPremiumPurchaseResultComposer)).GetUInt32());
    }

    private static RewardTrack ChatTrack()
    {
        var track = new RewardTrack("introduction", "blue", 0, null, null, true, 1.5, 25, 0, 25);
        track.AddTask(new RewardTrackTask("chat_with_users", RewardTrackActions.ChatWithSomeone, "", false, 1, new[]
        {
            new RewardTrackLevel(5, 10, false),
            new RewardTrackLevel(25, 20, false),
            new RewardTrackLevel(100, 40, false)
        }));
        track.AddPrize(new RewardTrackPrize("track_champ", 50, 4, "badge", "ACH_RewardTracksCompleted1", 1, false, 1));
        track.AddPrize(new RewardTrackPrize("track_champ_premium", 200, 4, "badge", "ACH_RewardTracksCompleted2", 1, true, 2));

        return track;
    }

    private static string RevisionPath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Resources", "Revisions", fileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(fileName);
    }

    private sealed class RecordingPacket : IOutgoingPacket
    {
        public List<object> Writes { get; } = new();
        public int MessageId
        {
            get; set;
        }
        public ReadOnlyMemory<byte> Buffer => ReadOnlyMemory<byte>.Empty;
        public void WriteByte(byte value) => Writes.Add(value);
        public void WriteShort(short value) => Writes.Add(value);
        public void WriteInt(int value) => Writes.Add(value);
        public void WriteInteger(int value) => Writes.Add(value);
        public void WriteUInt(uint value) => Writes.Add(value);
        public void WriteUInteger(uint value) => Writes.Add(value);
        public void WriteBool(bool value) => Writes.Add(value);
        public void WriteBoolean(bool value) => Writes.Add(value);
        public void WriteString(string value) => Writes.Add(value ?? "");
        public void WriteDouble(double value) => Writes.Add(value);
    }
}
