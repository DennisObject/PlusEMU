using Plus.Communication.Packets.Incoming.Rooms.Avatar;
using Plus.Communication.Packets.Incoming.Preferences;
using Plus.Communication.Packets.Incoming.Sound;
using Plus.Communication.Packets.Incoming.Users;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Plus.Core.FigureData;
using System.Reflection;
using Plus.HabboHotel.Users.Authentication;
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

        await new SetMessengerInviteStatusEvent(profiles).Parse(null!, HabbiconTestSupport.Incoming(true));
        await new SetSoundSettingsEvent(profiles).Parse(null!, HabbiconTestSupport.Incoming(-1, 50, 101));

        await new SetChatStylePreferenceEvent(profiles).Parse(null!, HabbiconTestSupport.Incoming(42));
        await new SetUIFlagsEvent(profiles).Parse(null!, HabbiconTestSupport.Incoming(1));
        Assert.Equal(42, profiles.Bubble);
        Assert.Equal(1, profiles.FriendBar);

        Assert.True(profiles.Invites);
        Assert.Equal(new SoundVolumeRequest(-1, 50, 101), profiles.Volumes);
        Assert.Equal(new("f", "hd-1"), profiles.Figure);
        Assert.Equal("hello", profiles.Motto);
        Assert.True(profiles.Focus);
    }

    [Fact]
    public void MutedUserCannotChangeMotto()
    {
        var habbo = new Habbo { Id = 7, Motto = "original", TimeMuted = 10 };
        var (session, sent) = HabbiconTestSupport.Client(habbo);
        var profiles = new UserProfileService(null!, null!, null!, null!, null!, TimeProvider.System, null!, null!, new AccountSessionGate());

        profiles.ChangeMotto(session, "changed");

        Assert.Equal("original", habbo.Motto);
        Assert.Single(sent);
    }

    [Fact]
    public void FailedPreferenceWriteKeepsThePreviousLiveSetting()
    {
        var habbo = new Habbo { Id = 7, FocusPreference = false };
        var (session, _) = HabbiconTestSupport.Client(habbo);
        var profiles = new UserProfileService(null!, null!, null!, null!, new FailingDatabase(), TimeProvider.System, null!, null!, null!);
        Assert.Throws<InvalidOperationException>(() => profiles.SetFocusPreference(session, true));
        Assert.False(habbo.FocusPreference);
    }

    [Fact]
    public void MottoThrottleUsesUtcAndIncludesTheTwoSecondBoundary()
    {
        var clock = new FixedClock();
        var habbo = new Habbo
        {
            Id = 7,
            Motto = "original",
            MottoUpdateWarnings = 24,
            LastMottoUpdatedAt = clock.GetUtcNow().AddSeconds(-2).ToOffset(TimeSpan.FromHours(2))
        };
        var (session, sent) = HabbiconTestSupport.Client(habbo);
        var profiles = new UserProfileService(null!, null!, null!, null!, new FailingDatabase(), clock, null!, null!, new AccountSessionGate());
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
    }

    [Fact]
    public void FailedMannequinPersistenceLeavesProfileAndPacketsUntouched()
    {
        var habbo = new Habbo { Id = 7, Look = "old-look", Gender = "M", Clothing = new() };
        var (session, sent) = HabbiconTestSupport.Client(habbo);
        var figures = DispatchProxy.Create<IFigureDataManager, FigureProxy>();
        var profiles = new UserProfileService(figures, null!, null!, null!, new FailingDatabase(),
            TimeProvider.System, null!, null!, new AccountSessionGate());

        Assert.Throws<InvalidOperationException>(() => profiles.ApplyMannequin(session, new("F", "new-look")));

        Assert.Equal("old-look", habbo.Look);
        Assert.Equal("M", habbo.Gender);
        Assert.Empty(sent);
    }

    public class FigureProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name == "ProcessFigure" ? args![0] : throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class RecordingProfiles : IUserProfileService
    {
        public FigureUpdateRequest? Figure { get; private set; }
        public string? Motto { get; private set; }
        public bool Focus { get; private set; }
        public int Bubble { get; private set; }
        public int FriendBar { get; private set; }
        public bool Invites { get; private set; }
        public SoundVolumeRequest? Volumes { get; private set; }
        public void ShowUserObject(GameClient session) { }
        public Task SetChatPreference(GameClient session, bool enabled) => Task.CompletedTask;
        public Task SetMessengerInvitePreference(GameClient session, bool enabled)
        {
            Invites = enabled;

            return Task.CompletedTask;
        }
        public Task SetSoundVolumes(GameClient session, SoundVolumeRequest request)
        {
            Volumes = request;

            return Task.CompletedTask;
        }
        public void UpdateFigure(GameClient session, FigureUpdateRequest request) => Figure = request;
        public void ApplyMannequin(GameClient session, FigureUpdateRequest request) => Figure = request;
        public void ChangeMotto(GameClient session, string motto) => Motto = motto;
        public void SetFocusPreference(GameClient session, bool enabled) => Focus = enabled;
        public Task SetChatStylePreference(GameClient session, int bubbleId)
        {
            Bubble = bubbleId;

            return Task.CompletedTask;
        }
        public void SetFriendBarState(GameClient session, int state) => FriendBar = state;
    }
}
