using Plus.Communication.Packets.Incoming.Quests;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class RewardTrackHandlerTests
{
    [Fact]
    public async Task TrackListAndPremiumHandlersDelegateOnlyToTheRequiredManager()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        var manager = new RecordingRewards();
        await new GetRewardTracksEvent(manager).Parse(client, HabbiconTestSupport.Incoming());
        var premium = HabbiconTestSupport.Incoming("introduction");
        await new PurchaseRewardTrackPremiumEvent(manager).Parse(client, premium);
        Assert.Equal(new[] { "List", "Premium introduction" }, manager.Calls);
        Assert.Same(client, manager.Session);
        Assert.False(premium.HasDataRemaining());
        Assert.Empty(sent);
    }

    [Fact]
    public async Task ClaimFullyDecodesAndKeepsTheManagersPendingTaskAndFailure()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        var manager = new RecordingRewards();
        var packet = HabbiconTestSupport.Incoming("introduction", "track_champ");
        var claim = new ClaimRewardTrackPrizeEvent(manager).Parse(client, packet);
        Assert.Same(manager.Completion.Task, claim);
        Assert.False(claim.IsCompleted);
        Assert.Equal(new[] { "Claim introduction track_champ" }, manager.Calls);
        Assert.Same(client, manager.Session);
        Assert.False(packet.HasDataRemaining());
        Assert.Empty(sent);
        manager.Completion.SetException(new InvalidOperationException("claim failed"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => claim);
        Assert.Equal("claim failed", error.Message);
    }

    private sealed class RecordingRewards : IRewardTrackManager
    {
        public List<string> Calls { get; } = [];
        public GameClient? Session;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void SendTracks(GameClient session)
        {
            Session = session;
            Calls.Add("List");
        }
        public void PurchasePremium(GameClient session, string trackId)
        {
            Session = session;
            Calls.Add("Premium " + trackId);
        }
        public Task Claim(GameClient session, string trackId, string prizeId)
        {
            Session = session;
            Calls.Add($"Claim {trackId} {prizeId}");

            return Completion.Task;
        }
        public void Progress(GameClient session, string actionType, int amount = 1) => throw new NotSupportedException();
    }
}
