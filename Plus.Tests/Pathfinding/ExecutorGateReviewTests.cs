using System.Reflection;
using Plus.HabboHotel;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(true)]
    public void AcceptedGuildMemberStepOpensTheGateAndClosesItAfterDeparture(bool v2)
    {
        var gate = ReviewGuildGate(member: true);
        var actor = ReviewGateActor(v2);
        actor.MoveTo(1, 1);
        ExecutorTick();
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal(7, gate.InteractingUser);
        Assert.Equal(4, gate.UpdateCounter);
        Assert.True(gate.UpdateNeeded);
        Assert.Equal("1,1,0", actor.Statusses["mv"]);
        ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        actor.MoveTo(2, 1);
        ExecutorTick();
        ExecutorTick();
        Assert.Equal((2, 1), (actor.X, actor.Y));

        for (var cycle = 0; cycle < 8; cycle++) {
            ExecutorTick();
        }

        Assert.Equal("0", gate.LegacyDataString);
        Assert.False(gate.UpdateNeeded);
    }

    [Theory]
    [InlineData(true)]
    public void DeniedGuildMemberStepLeavesTheGateClosedAndUntouched(bool v2)
    {
        var gate = ReviewGuildGate(member: false);
        var actor = ReviewGateActor(v2);
        actor.MoveTo(1, 1);

        for (var cycle = 0; cycle < 3; cycle++) {
            ExecutorTick();
        }

        Assert.Equal((0, 1), (actor.X, actor.Y));
        Assert.False(actor.HasStatus("mv"));
        Assert.Equal("0", gate.LegacyDataString);
        Assert.Equal(0, gate.InteractingUser);
        Assert.Equal(0, gate.UpdateCounter);
        Assert.False(gate.UpdateNeeded);
    }

    [Fact]
    public void PrivilegedSeatGoalReplacementSkipsTheOldLandingAndHooks()
    {
        Add(10, 1, 1, z: .25, seat: true);
        var events = ExecutorWalkEvents();
        var actor = ExecutorActor(0, 1);
        actor.AllowOverride = true;
        actor.MoveTo(1, 1);
        ExecutorTick();
        actor.MoveTo(0, 2);
        ExecutorTick();
        Assert.Equal((0, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Empty(events);
        Assert.False(actor.HasStatus("sit"));
        Assert.Contains("/mv 0,2,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((0, 2), (actor.X, actor.Y));
    }

    private Item ReviewGuildGate(bool member)
    {
        var group = new Group(23, "gate", "", "", RoomId, 7, DateTimeOffset.UnixEpoch, 0, 1, 1, 0, false,
            GroupMembershipSnapshot.Empty);

        if (member) {
            ((List<int>)typeof(Group).GetField("_members", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(group)!).Add(7);
        }

        var groups = Proxy<IGroupManager>((method, args) =>
        {
            Assert.Equal("TryGetGroup", method);
            args[1] = group;

            return (int)args[0]! == group.Id;
        });
        _groupLookup = id => id == group.Id ? group : null;
        var previous = ((TestProxy)_gameField.GetValue(null)!).Call;
        _gameField.SetValue(null, Proxy<IGame>((method, args) =>
            method == "get_GroupManager" ? groups : previous(method, args)));
        var gate = Add(10, 1, 1, type: InteractionType.GuildGate);
        InitializeNativeState(gate);
        gate.GroupId = group.Id;

        return gate;
    }

    private RoomUser ReviewGateActor(bool v2)
    {
        if (v2) {
            return ExecutorActor(0, 1);
        }

        var actor = Viewer(0, 1);
        actor.UserId = 7;
        _room.GetGameMap().AddUserToMap(actor, actor.Coordinate);

        return actor;
    }
}
