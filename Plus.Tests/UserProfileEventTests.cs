using Plus.Communication.Packets.Incoming.Rooms.Avatar;
using Plus.Communication.Packets.Incoming.Users;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class UserProfileEventTests
{
    [Fact]
    public async Task HandlersDecodeAndDelegateTypedProfileChanges()
    {
        var profiles = new RecordingProfiles();
        await new UpdateFigureDataEvent(profiles).Parse(null!, HabbiconTestSupport.Incoming("f", "hd-1"));
        await new ChangeMottoEvent(profiles).Parse(null!, HabbiconTestSupport.Incoming("hello"));
        await new SetUserFocusPreferenceEvent(profiles).Parse(null!, HabbiconTestSupport.Incoming(true));

        Assert.Equal(new("f", "hd-1"), profiles.Figure);
        Assert.Equal("hello", profiles.Motto);
        Assert.True(profiles.Focus);
    }

    [Fact]
    public void MutedUserCannotChangeMotto()
    {
        var habbo = new Habbo { Id = 7, Motto = "original", TimeMuted = 10 };
        var (session, sent) = HabbiconTestSupport.Client(habbo);
        var profiles = new UserProfileService(null!, null!, null!, null!, null!, TimeProvider.System);

        profiles.ChangeMotto(session, "changed");

        Assert.Equal("original", habbo.Motto);
        Assert.Single(sent);
    }

    [Fact]
    public void FailedPreferenceWriteKeepsThePreviousLiveSetting()
    {
        var habbo = new Habbo { Id = 7, FocusPreference = false };
        var (session, _) = HabbiconTestSupport.Client(habbo);
        var profiles = new UserProfileService(null!, null!, null!, null!, new FailingDatabase(), TimeProvider.System);
        Assert.Throws<InvalidOperationException>(() => profiles.SetFocusPreference(session, true));
        Assert.False(habbo.FocusPreference);
    }

    [Fact]
    public void MottoThrottleUsesUtcAndIncludesTheTwoSecondBoundary()
    {
        var clock = new FixedClock();
        var habbo = new Habbo { Id = 7, Motto = "original", MottoUpdateWarnings = 24,
            LastMottoUpdatedAt = clock.GetUtcNow().AddSeconds(-2).ToOffset(TimeSpan.FromHours(2)) };
        var (session, sent) = HabbiconTestSupport.Client(habbo);
        var profiles = new UserProfileService(null!, null!, null!, null!, new FailingDatabase(), clock);
        profiles.ChangeMotto(session, "changed");
        Assert.Equal(25, habbo.MottoUpdateWarnings);
        Assert.True(habbo.SessionMottoBlocked);
        Assert.Equal("original", habbo.Motto);
        Assert.Empty(sent);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2042, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
    private sealed class FailingDatabase : Plus.Database.IDatabase
    {
        public bool IsConnected() => true;
        public System.Data.IDbConnection Connection() => throw new InvalidOperationException("Persistence failed");
        [Obsolete] public Plus.Database.Interfaces.IQueryAdapter GetQueryReactor() => throw new NotSupportedException();
    }

    private sealed class RecordingProfiles : IUserProfileService
    {
        public FigureUpdateRequest? Figure { get; private set; }
        public string? Motto { get; private set; }
        public bool Focus { get; private set; }
        public void UpdateFigure(GameClient session, FigureUpdateRequest request) => Figure = request;
        public void ChangeMotto(GameClient session, string motto) => Motto = motto;
        public void SetFocusPreference(GameClient session, bool enabled) => Focus = enabled;
    }
}
