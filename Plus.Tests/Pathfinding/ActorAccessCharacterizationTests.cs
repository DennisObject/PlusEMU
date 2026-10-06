using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.Tests.Performance;
using Xunit;

namespace Plus.Tests;

// Pins today's actor-access behaviour (§16.2) before membership moves behind ActorAccessResolver.
public partial class PlacedFurniRoomTests
{
    private static Group GuildGroup(params int[] members)
    {
        var group = (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group));
        RoomPerformanceFixture.SetField(group, "_members", new List<int>(members));
        RoomPerformanceFixture.SetField(group, "_administrators", new List<int>());

        return group;
    }

    private void UseGroups(Func<int, Group?> lookup)
    {
        _groupLookup = lookup;
        var groups = Proxy<IGroupManager>((method, args) =>
        {
            Assert.Equal("TryGetGroup", method);
            var group = lookup((int)args[0]!);
            args[1] = group;

            return group != null;
        });
        var previous = (IGame)_gameField.GetValue(null)!;
        _gameField.SetValue(null, Proxy<IGame>((method, args) => method == "get_GroupManager" ? groups
            : typeof(IGame).GetMethod(method)!.Invoke(previous, args)));
    }

    private Item AccessGate(InteractionType kind, bool walkable = false)
    {
        var gate = Furni(10, kind, WiredBoxType.None);
        gate.Definition.Height = 0;
        gate.Definition.Width = gate.Definition.Length = 1;
        gate.Definition.Walkable = walkable;
        gate.GroupId = 7;
        gate.UserId = 7;
        InitializeNativeState(gate);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, gate, 1, 1, 0, true, false, false));

        return gate;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AccessExecutorLetsOnlyGroupMembersEnterAGuildGate(bool member)
    {
        AccessGate(InteractionType.GuildGate);
        UseGroups(id => id == 7 ? GuildGroup(member ? [7] : []) : null);
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1);
        ExecutorTick();
        ExecutorTick();
        Assert.Equal(member ? (1, 1) : (0, 1), (actor.X, actor.Y));
        Assert.Equal(member, actor.Movement.Profile.IsMember(7));
        Assert.False(actor.HasStatus("mv"));
    }

    [Fact]
    public void AccessExecutorRefreshesMembershipAtCommitSoRevokedStepsDoNotLand()
    {
        AccessGate(InteractionType.GuildGate);
        var group = GuildGroup(7);
        UseGroups(id => id == 7 ? group : null);
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1);
        ExecutorTick();
        Assert.Equal("1,1,0", actor.Statusses["mv"]);
        var version = actor.Movement.Profile.CapabilityVersion;
        RoomPerformanceFixture.SetField(group, "_members", new List<int>());
        ExecutorTick();
        Assert.Equal((0, 1), (actor.X, actor.Y));
        Assert.False(actor.Movement.Profile.IsMember(7));
        Assert.True(actor.Movement.Profile.CapabilityVersion > version);
    }

    [Fact]
    public void AccessExecutorTreatsAnUnknownGroupAsNotAMember()
    {
        AccessGate(InteractionType.GuildGate);
        UseGroups(_ => null);
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1);
        ExecutorTick();
        ExecutorTick();
        Assert.Equal((0, 1), (actor.X, actor.Y));
    }

    [Theory]
    [InlineData(InteractionType.GateVip)]
    [InlineData(InteractionType.PetBreedingBox)]
    [InlineData(InteractionType.FootballGate)]
    public void AccessGatesWithoutGuildRulesAdmitEveryActorInBothEngines(InteractionType kind)
    {
        var gate = AccessGate(kind, walkable: true);
        var actor = ExecutorActor(0, 1);
        Assert.True(_room.GetGameMap().IsValidStep2(actor, new(0, 1), new(1, 1), true, false));
        Assert.Equal("0", gate.LegacyDataString);
        actor.MoveTo(1, 1);
        ExecutorTick();
        ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.False(actor.Movement.Profile.IsMember(7));
    }

    [Fact]
    public void AccessLegacyGuildGateStillOpensForMembersAtExecutionOnly()
    {
        var gate = AccessGate(InteractionType.GuildGate);
        _groupLookup = id => id == 7 ? GuildGroup(7) : null;
        var previous = (IGame)_gameField.GetValue(null)!;
        _gameField.SetValue(null, Proxy<IGame>((method, args) => method == "get_GroupManager"
            ? throw new InvalidOperationException("Legacy guild gate must not use the global group manager.")
            : typeof(IGame).GetMethod(method)!.Invoke(previous, args)));
        var user = Viewer(0, 1);
        Assert.True(_room.GetGameMap().IsValidStep(new(0, 1), new(1, 1), true, false));
        Assert.Equal("0", gate.LegacyDataString);
        Assert.True(_room.GetGameMap().IsValidStep2(user, new(0, 1), new(1, 1), true, false));
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Equal(7, gate.InteractingUser);
        Assert.Equal(4, gate.UpdateCounter);
    }
}
