using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Speech;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void MovementProfileServiceRefreshesLiveAuthorityWithoutChangingGridVersion()
    {
        var grid = ProfileGrid();
        grid.GroupId[0] = grid.GroupId[1] = 7;
        var actor = Viewer();
        var allowed = false;
        var calls = 0;
        _client.GetHabbo().Id = 99;
        var service = new MovementProfileService(_room, grid, new(), (group, habbo) =>
        { Assert.Equal(7, group); Assert.Equal(99, habbo); calls++; return allowed; });
        var profile = service.Refresh(actor);
        Assert.Same(actor.Movement.Profile, profile);
        Assert.False(profile.IsMember(7));
        Assert.Equal(0, profile.CapabilityVersion);
        allowed = true;
        service.Refresh(actor);
        Assert.True(profile.IsMember(7));
        Assert.Equal(1, profile.CapabilityVersion);
        service.Refresh(actor);
        Assert.Equal(1, profile.CapabilityVersion);
        allowed = false;
        service.Refresh(actor);
        Assert.False(profile.IsMember(7));
        Assert.Equal(2, profile.CapabilityVersion);
        Assert.Equal(4, calls);
    }

    [Fact]
    public void MovementProfileServiceDeduplicatesGroupsAndInvalidatesOnlyOnGridPublication()
    {
        var grid = ProfileGrid();
        grid.GroupId[0] = grid.GroupId[1] = 7;
        grid.GroupId[2] = 9;
        var actor = Viewer();
        var calls = new List<int>();
        var service = new MovementProfileService(_room, grid, new(), (group, habbo) =>
        { Assert.Equal(7, habbo); calls.Add(group); return true; });
        service.Refresh(actor);
        Assert.Equal(new[] { 7, 9 }, calls.Order().ToArray());
        Array.Fill(grid.GroupId, 11);
        calls.Clear();
        service.Refresh(actor);
        Assert.Equal(new[] { 7, 9 }, calls.Order().ToArray());
        grid.Version++;
        calls.Clear();
        service.Refresh(actor);
        Assert.Equal(new[] { 11 }, calls);
        Assert.True(actor.Movement.Profile.IsMember(11));
        Array.Clear(grid.GroupId);
        grid.Version++;
        calls.Clear();
        service.Refresh(actor);
        Assert.Empty(calls);
    }

    [Fact]
    public void MovementProfileServiceCachesAnEmptyGridUntilItsVersionChanges()
    {
        var grid = ProfileGrid();
        var actor = Viewer();
        var calls = 0;
        var service = new MovementProfileService(_room, grid, new(), (_, _) => { calls++; return true; });

        for (var i = 0; i < 1000; i++) {
            service.Refresh(actor);
        }

        Assert.Equal(0, calls);
        grid.GroupId[0] = 7;
        service.Refresh(actor);
        Assert.Equal(0, calls);
        Assert.False(actor.Movement.Profile.IsMember(7));
        grid.Version++;
        service.Refresh(actor);
        Assert.Equal(1, calls);
        Assert.True(actor.Movement.Profile.IsMember(7));
    }

    [Fact]
    public void MovementProfileServiceClearsCachedAccessWithoutHabboIdentity()
    {
        var grid = ProfileGrid();
        grid.GroupId[0] = 7;
        var actor = Viewer();
        actor.Movement.Profile.SetMembership(7, true);
        actor.BotData = ProfileBot(false);
        var service = new MovementProfileService(_room, grid, new(), (_, _) =>
            throw new InvalidOperationException("Bots have no Habbo authority"));
        var profile = service.Refresh(actor);
        Assert.False(profile.IsMember(7));
        Assert.Equal(2, profile.CapabilityVersion);
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, true, false)]
    [InlineData(false, false, false, false)]
    public void MovementProfileServicePreservesTemporaryBotPrivileges(bool temporary, bool allowOverride,
        bool legacyOverride, bool ignoreUsers)
    {
        var actor = Viewer();
        actor.BotData = ProfileBot(temporary);
        actor.AllowOverride = allowOverride;
        var profile = new MovementProfileService(_room, ProfileGrid(), new(), (_, _) => false).Refresh(actor);
        Assert.Equal(legacyOverride, profile.LegacyOverride);
        Assert.Equal(ignoreUsers, profile.IgnoreUsers);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void MovementProfileServiceAppliesRiderHeightPolicy(bool riding, bool ignoreHeight, bool expected)
    {
        var actor = Viewer();
        actor.RidingHorse = riding;
        var settings = new PathfindingSettings { RidersIgnoreHeight = ignoreHeight };
        var profile = new MovementProfileService(_room, ProfileGrid(), settings, (_, _) => false).Refresh(actor);
        Assert.Equal(expected, profile.IgnoreStepHeight);
    }

    [Fact]
    public void MovementProfileServiceRefreshesRoomFlagsAndKeepsInteractionAuthorization()
    {
        var actor = Viewer();
        actor.AllowOverride = true;
        actor.Movement.Profile.Interaction = new(1, 0, 1, 1);
        _room.RoomBlockingEnabled = true;
        _room.GetGameMap().DiagonalEnabled = false;
        var service = new MovementProfileService(_room, ProfileGrid(), new(), (_, _) => false);
        var profile = service.Refresh(actor);
        Assert.True(profile.LegacyOverride);
        Assert.True(profile.Walkthrough);
        Assert.False(profile.DiagonalEnabled);
        Assert.Equal(new InteractionAuthorization(1, 0, 1, 1), profile.Interaction);
        actor.AllowOverride = false;
        _room.RoomBlockingEnabled = false;
        _room.GetGameMap().DiagonalEnabled = true;
        service.Refresh(actor);
        Assert.False(profile.LegacyOverride);
        Assert.False(profile.Walkthrough);
        Assert.True(profile.DiagonalEnabled);
        Assert.Equal(new InteractionAuthorization(1, 0, 1, 1), profile.Interaction);
    }

    private static NavGrid ProfileGrid() => new(4, 4, new double[16], new SquareState[16]);

    private static RoomBot ProfileBot(bool temporary)
    {
        var speeches = new List<RandomSpeech>();

        return new RoomBot(-1, RoomId, "generic", "freeroam", "Profile", "", "hd-180-1",
            1, 1, 0, 0, 0, 0, 0, 0, ref speeches, "M", 0, 7, false, 60, false, 0)
        { IsTemporary = temporary };
    }
}
