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
        var profiles = new UserProfileService(null!, null!, null!, null!, null!);

        profiles.ChangeMotto(session, "changed");

        Assert.Equal("original", habbo.Motto);
        Assert.Single(sent);
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
