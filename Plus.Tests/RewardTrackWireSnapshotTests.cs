using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Quests;
using Plus.HabboHotel.Quests;
using Xunit;

namespace Plus.Tests;

public class RewardTrackWireSnapshotTests
{
    [Fact]
    public void FullTrackGoldenWireOrderAndRules()
    {
        var track = Track();
        var state = new UserRewardTrackState("track", 60, false);
        state.SetStoredCount("t1", 7);
        state.MarkClaimed("p1");

        var packet = Write(new RewardTracksComposer(false, [RewardTrackWireSnapshot.Capture(track, state)], true));

        Assert.Equal(new object[]
        {
            false, 1,
            "track", "blue", 60, true, 1.5, 25, 0, 25, false, true, false,
            1,
            "t1", "chat", "", 7, false, 2,
            5, 10, false,
            10, 20, false,
            2,
            "p1", 50, (short)4, "badge", "ACH_1", 1, false, true, true,
            "p2", 200, (short)4, "badge", "ACH_2", 1, true, false, false,
            true,
        }, packet);
    }

    [Theory]
    [InlineData(false, false, false, false, false, false, false)]
    [InlineData(false, true, false, true, false, false, false)]
    [InlineData(true, true, false, true, false, true, false)]
    [InlineData(true, true, true, true, true, true, true)]
    [InlineData(true, false, true, false, false, true, true)]
    public void PremiumCombinationsFollowTheCompletionRules(bool premium, bool p1Claimed, bool p2Claimed, bool complete, bool premiumComplete, bool p2Reachable, bool p2ClaimedExpected)
    {
        var track = Track();
        var state = new UserRewardTrackState("track", 300, premium);
        if (p1Claimed) state.MarkClaimed("p1");
        if (p2Claimed) state.MarkClaimed("p2");

        var snapshot = RewardTrackWireSnapshot.Capture(track, state);

        Assert.Equal(complete, snapshot.Complete);
        Assert.Equal(premiumComplete, snapshot.PremiumComplete);
        Assert.Equal(p2Reachable, snapshot.Prizes[1].Reachable);
        Assert.Equal(p2ClaimedExpected, snapshot.Prizes[1].Claimed);
    }

    [Fact]
    public void TrackWithoutPremiumIsPremiumCompleteOnceItIsNotPremium()
    {
        var track = new RewardTrack("plain", "blue", 1, null, null, false, 0, 0, 0, 0);
        track.AddPrize(new RewardTrackPrize("p1", 5, 4, "badge", "ACH_1", 1, false, 1));
        var state = new UserRewardTrackState("plain", 0, false);

        var snapshot = RewardTrackWireSnapshot.Capture(track, state);

        Assert.False(snapshot.Complete);
        Assert.True(snapshot.PremiumComplete);
        Assert.False(snapshot.HasPremium);
        Assert.Equal(new object[]
        {
            false, 1, "plain", "blue", 0, false, false, false, true,
            0,
            1, "p1", 5, (short)4, "badge", "ACH_1", 1, false, false, false,
            true,
        }, Write(new RewardTracksComposer(false, [snapshot], true)));
    }

    [Fact]
    public void SourceMutationAfterCaptureDoesNotChangeRecomposition()
    {
        var track = Track();
        var state = new UserRewardTrackState("track", 60, false);
        state.SetStoredCount("t1", 7);
        state.MarkClaimed("p1");
        var snapshot = RewardTrackWireSnapshot.Capture(track, state);
        var composer = new RewardTracksComposer(false, [snapshot], true);
        var first = Write(composer);

        track.AddPrize(new RewardTrackPrize("p3", 1, 4, "badge", "ACH_3", 1, false, 3));
        track.AddTask(new RewardTrackTask("t2", "late", "", false, 2, []));
        state.ApplyPremium(999);
        state.SetStoredCount("t1", 0);
        state.MarkClaimed("p2");

        Assert.Equal(3, track.Prizes.Count);
        Assert.Equal(first, Write(composer));
        Assert.Equal(first, Write(composer));
    }

    [Fact]
    public void ListMutationAfterCaptureDoesNotChangeTheComposedBytes()
    {
        var snapshot = RewardTrackWireSnapshot.Capture(Track(), new UserRewardTrackState("track", 60, false));
        var list = new List<RewardTrackWireTrack> { snapshot };
        var captured = ImmutableArray.CreateRange(list);
        var first = Write(new RewardTracksComposer(false, captured, true));

        list.Clear();
        list.Add(RewardTrackWireSnapshot.Capture(Track(), new UserRewardTrackState("track", 999, true)));

        Assert.Equal(first, Write(new RewardTracksComposer(false, captured, true)));
    }

    [Fact]
    public void EmptyTracksWriteOnlyTheFlagsAndZeroCount()
    {
        Assert.Equal(new object[] { true, 0, true }, Write(new RewardTracksComposer(true, ImmutableArray<RewardTrackWireTrack>.Empty, true)));
        Assert.Equal(new object[] { false, 0, false }, Write(new RewardTracksComposer(false, ImmutableArray<RewardTrackWireTrack>.Empty, false)));
    }

    private static RewardTrack Track()
    {
        var track = new RewardTrack("track", "blue", 1, null, null, true, 1.5, 25, 0, 25);
        track.AddTask(new RewardTrackTask("t1", "chat", "", false, 1, [new RewardTrackLevel(10, 20, false), new RewardTrackLevel(5, 10, false)]));
        track.AddPrize(new RewardTrackPrize("p1", 50, 4, "badge", "ACH_1", 1, false, 1));
        track.AddPrize(new RewardTrackPrize("p2", 200, 4, "badge", "ACH_2", 1, true, 2));
        return track;
    }

    private static List<object> Write(RewardTracksComposer composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);
        return packet.Writes;
    }
}
