using System.Collections.Concurrent;
using System.Reflection;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Plus.HabboHotel.Users.Permissions;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    // V2 roller identity follows the current interaction type (the legacy Item.IsRoller is never set):
    // a destination that is a roller keeps the carried Z, any other destination drops by the roller height.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void V2RollerIdentityFollowsTheCurrentInteractionType(bool destinationIsRoller)
    {
        ExecutorRoller(10, 0, 1);
        var destination = ExecutorFloor(11, 1, 1, height: .5);
        InstallRollerChainEngine(PathfindingEngine.V2);
        var actor = RollerChainActor(1, 0, .5);
        destination.Definition.InteractionType = destinationIsRoller ? InteractionType.Roller : InteractionType.None;
        ExecutorTick();
        EnableExecutorRollers();
        ExecutorTick();
        Assert.Equal((1, 1, destinationIsRoller ? .5 : 0d), (actor.X, actor.Y, actor.Z));
    }

    // V2 cycles registered rollers on its own; it does not depend on the legacy GotRollers flag.
    [Fact]
    public void V2RollersCycleWithoutTheLegacyGotRollersFlag()
    {
        ExecutorRoller(10, 0, 1);
        InstallRollerChainEngine(PathfindingEngine.V2);
        var actor = RollerChainActor(1, 0, .5);
        ExecutorTick();
        _room.GetRoomItemHandler().SetSpeed(0);
        Assert.False(_room.GetRoomItemHandler().GotRollers);
        ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
    }

    [Theory]
    [InlineData(PathfindingEngine.V2, false)]
    [InlineData(PathfindingEngine.V2, true)]
    public void RollerChainBlocksUpstreamActorWhenStaticFurniturePreventsCargoExit(
        PathfindingEngine engine, bool reverseIds)
    {
        ExecutorRoller(reverseIds ? 11u : 10u, 0, 1);
        ExecutorRoller(reverseIds ? 10u : 11u, 1, 1);
        ExecutorRoller(20, 2, 1);
        var cargo = ExecutorFloor(12, 1, 1, z: 1, height: 1);
        var blocker = Add(13, 2, 0, z: 1, height: 3, stackable: false, length: 2);
        InstallRollerChainEngine(engine);
        var actor = RollerChainActor(1, 0, .5);
        ExecutorTick();
        EnableExecutorRollers();
        ExecutorTick();
        Assert.Equal((0, 1, .5), (actor.X, actor.Y, actor.Z));
        Assert.Equal((1, 1, 1d), (cargo.GetX, cargo.GetY, cargo.GetZ));
        Assert.Equal((2, 0, 1d), (blocker.GetX, blocker.GetY, blocker.GetZ));
        Assert.DoesNotContain(_client.Packets, packet => ExecutorIsAvatarSlide(packet, actor.VirtualId));
        Assert.DoesNotContain(_client.Sent, header => header == ServerPacketHeader.SlideObjectBundleComposer);
    }

    [Theory]
    [InlineData(PathfindingEngine.V2, false)]
    [InlineData(PathfindingEngine.V2, true)]
    public void RollerChainAdvancesEveryIdleActorEveryCycleWithoutRequiringAnEmptyTileBetweenActors(
        PathfindingEngine engine, bool reverseIds)
    {
        PrepareRollerChain(8, reverseIds);
        InstallRollerChainEngine(engine);
        var actors = new[] { RollerChainActor(1, 0, .5), RollerChainActor(2, 1, .5), RollerChainActor(3, 2, .5) };
        ExecutorTick();
        EnableExecutorRollers();

        for (var cycle = 1; cycle <= 3; cycle++) {
            ExecutorTick();

            for (var index = 0; index < actors.Length; index++) {
                Assert.Equal((index + cycle, 1, .5), (actors[index].X, actors[index].Y, actors[index].Z));
            }

            Assert.Equal(3, _client.Packets.Count(packet => packet.Header == ServerPacketHeader.SlideObjectBundleComposer));
            Assert.All(actors, actor => Assert.Single(_client.Packets, packet => ExecutorIsAvatarSlide(packet, actor.VirtualId)));
        }
    }

    [Theory]
    [InlineData(PathfindingEngine.V2, false)]
    [InlineData(PathfindingEngine.V2, true)]
    public void RollerChainAdvancesEveryCargoEveryCycleWithoutRequiringAnEmptyTileBetweenItems(
        PathfindingEngine engine, bool reverseIds)
    {
        PrepareRollerChain(8, reverseIds);
        var cargo = Enumerable.Range(0, 3).Select(x => ExecutorFloor((uint)(200 + x), x, 1, z: 1, height: .25)).ToArray();
        InstallRollerChainEngine(engine);
        RollerChainActor(1, 7, 0);
        ExecutorTick();
        EnableExecutorRollers();

        for (var cycle = 1; cycle <= 3; cycle++) {
            ExecutorTick();

            for (var index = 0; index < cargo.Length; index++) {
                Assert.Equal((index + cycle, 1, 1d), (cargo[index].GetX, cargo[index].GetY, cargo[index].GetZ));
                var slide = ExecutorCargoSlide(cargo[index]);
                Assert.Equal((index + cycle - 1, index + cycle, "1", "1"), (slide.FromX, slide.ToX, slide.FromZ, slide.ToZ));
            }

            Assert.Equal(3, _client.Packets.Count(packet => packet.Header == ServerPacketHeader.SlideObjectBundleComposer));
        }
    }

    [Theory]
    [InlineData(PathfindingEngine.V2)]
    public void EmptyRollerChainKeepsPositionsAndProducesNoTransportPackets(PathfindingEngine engine)
    {
        PrepareRollerChain(8, false);
        InstallRollerChainEngine(engine);
        var observer = RollerChainActor(1, 7, 0);
        ExecutorTick();
        EnableExecutorRollers();

        for (var cycle = 0; cycle < 3; cycle++) {
            ExecutorTick();
            Assert.Equal((7, 1, 0d), (observer.X, observer.Y, observer.Z));
            Assert.DoesNotContain(_client.Sent, header => header == ServerPacketHeader.SlideObjectBundleComposer);
        }

        Assert.Equal(Enumerable.Range(0, 7).ToArray(), _room.GetRoomItemHandler().GetRollers().Select(item => item.GetX).Order().ToArray());
    }

    [Theory]
    [InlineData(PathfindingEngine.V2, .5)]
    [InlineData(PathfindingEngine.V2, 1.1234)]
    public void RollerChainExitOntoFloorPreservesTheExactCarriedZFormula(PathfindingEngine engine, double startZ)
    {
        ExecutorRoller(10, 0, 1);
        InstallRollerChainEngine(engine);
        var actor = RollerChainActor(1, 0, startZ);
        ExecutorTick();
        EnableExecutorRollers();
        ExecutorTick();
        Assert.Equal((1, 1, startZ - .5), (actor.X, actor.Y, actor.Z));
        var slide = ExecutorAvatarSlide(actor);
        Assert.Equal(startZ.ToString(System.Globalization.CultureInfo.InvariantCulture), slide.FromZ);
        Assert.Equal((startZ - .5).ToString(System.Globalization.CultureInfo.InvariantCulture), slide.ToZ);

        if (engine == PathfindingEngine.V2 && startZ != .5) {
            Assert.Null(actor.Movement.CurrentRef);
        }
    }

    private void PrepareRollerChain(int width, bool reverseIds)
    {
        var rows = string.Join('\r', Enumerable.Repeat(new string('0', width), 4));
        Set("_gamemap", new Gamemap(_room, new RoomModel("roller-chain", 0, 0, 0, 0, rows, 0, 0, false), TestLogging.Navigation, TestRoomSettings.Empty, new TestGroupManager(id => _groupLookup(id)), _database, TestNavigationRewards.Instance));
        _room.GetGameMap().GenerateMaps();

        for (var x = 0; x < width - 1; x++) {
            ExecutorRoller((uint)(reverseIds ? 100 - x : 10 + x), x, 1);
        }
    }

    private void InstallRollerChainEngine(PathfindingEngine engine)
    {
        if (engine == PathfindingEngine.Legacy) {
            return;
        }

        var map = _room.GetGameMap();
        var navigation = new RoomNavigation(_room, map.StaticModel, new() { Engine = engine }, TestLogging.Navigation, new TestGroupManager(id => _groupLookup(id)), _database, TestNavigationRewards.Instance);
        typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(map, navigation);

        foreach (var item in _room.GetRoomItemHandler().GetFloor) {
            navigation.Inputs.Attach(item);
        }
    }

    private RoomUser RollerChainActor(int id, int x, double z)
    {
        var client = id == 1 ? _client : new TestClient();

        if (id != 1) {
            client.SetHabbo(new Habbo
            {
                Id = id + 6,
                Username = $"roller-{id}",
                CurrentRoom = _room,
                Access = Plus.HabboHotel.Permissions.UserAccess.Empty
            });
        }

        var habbo = client.GetHabbo();
        habbo.Effects = new EffectsComponent(new FixedTimeProvider(FixedTimeProvider.Epoch));
        habbo.HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);
        var actor = new RoomUser(habbo.Id, RoomId, id, _room, client, TestChatEmotions.Unused, TestRewardProgress.Unused) { UserId = habbo.Id, InternalRoomId = id, X = x, Y = 1, Z = z };
        var roster = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_room.GetRoomUserManager())!;
        Assert.True(roster.TryAdd(id, actor));

        if (_room.GetGameMap().Navigation is { UsesExecutor: true } navigation) {
            navigation.Admit(actor);
        }
        else {
            _room.GetGameMap().AddUserToMap(actor, new(x, 1));
        }

        return actor;
    }
}
