using System.Collections.Concurrent;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void RollerCarriesAnIdleActorOntoFloorWithMatchingSlideAndMembership()
    {
        var roller = ExecutorRoller(10, 0, 1);
        var actor = ExecutorRollerActor(0, 1, 0.5);
        var revision = actor.Movement.LocationRevision;
        EnableExecutorRollers();
        ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(_room.GetGameMap().Navigation!.Grid.Reference(5), actor.Movement.CurrentRef);
        Assert.True(actor.Movement.LocationRevision > revision);
        Assert.Contains(actor, _room.GetGameMap().GetRoomUsers(new(1, 1)));
        Assert.DoesNotContain(actor, _room.GetGameMap().GetRoomUsers(new(0, 1)));
        var slide = ExecutorAvatarSlide(actor);
        Assert.Equal((0, 1, 1, 1, roller.Id, "0.5", "0"), slide);
        Assert.False(actor.HasStatus("mv"));
    }

    // A status in the roll tick snaps the client to the destination; the slide alone must drive the glide.
    [Fact]
    public void RollerGlidesTheActorAndSendsItsStatusOnlyAfterTheRoll()
    {
        ExecutorRoller(10, 0, 1);
        var actor = ExecutorRollerActor(0, 1, 0.5);
        EnableExecutorRollers();
        ExecutorTick();
        ExecutorAvatarSlide(actor);
        Assert.False(ExecutorHasUpdate(actor));
        ExecutorTick();
        var update = ExecutorUpdate(actor);
        Assert.Equal((1, 1, "0"), (update.X, update.Y, update.Z));
        Assert.DoesNotContain("/mv ", update.Status);
    }

    [Fact]
    public void ChainedRollersGlideTheActorWithoutAStatusBetweenPulses()
    {
        ExecutorRoller(10, 0, 1);
        ExecutorRoller(11, 1, 1);
        var actor = ExecutorRollerActor(0, 1, 0.5);
        EnableExecutorRollers();
        ExecutorTick();
        Assert.False(ExecutorHasUpdate(actor));
        ExecutorTick();
        var slide = ExecutorAvatarSlide(actor);
        Assert.Equal((1, 1, 2, 1), (slide.FromX, slide.FromY, slide.ToX, slide.ToY));
        Assert.False(ExecutorHasUpdate(actor));
        ExecutorTick();
        var update = ExecutorUpdate(actor);
        Assert.Equal((2, 1, "0"), (update.X, update.Y, update.Z));
        Assert.DoesNotContain("/mv ", update.Status);
    }

    [Fact]
    public void RollerPublishesMovedCargoBeforeBindingTheCarriedActorToIt()
    {
        ExecutorRoller(10, 0, 1);
        var cargo = ExecutorFloor(11, 0, 1, z: 1, height: 2);
        var actor = ExecutorRollerActor(0, 1, 3);
        EnableExecutorRollers();
        ExecutorTick();
        var navigation = _room.GetGameMap().Navigation!;
        Assert.Equal((1, 1, 0.5), (cargo.GetX, cargo.GetY, cargo.GetZ));
        Assert.Equal((1, 1, 2.5), (actor.X, actor.Y, actor.Z));
        Assert.Equal(cargo.Id, actor.Movement.CurrentRef!.Value.SupportItemId);
        Assert.Equal(5, actor.Movement.CurrentRef.Value.Tile);
        Assert.Equal(2.5, navigation.Grid.WalkZ[5]);
        Assert.Equal(0.5, navigation.Inputs.AppliedRecords[cargo.Id].Z);
        Assert.Equal("2.5", ExecutorAvatarSlide(actor).ToZ);
    }

    [Fact]
    public void RollerPreservesExactCarriedZOffGraphAndTheNextMoveEscapes()
    {
        ExecutorRoller(10, 0, 1);
        var actor = ExecutorRollerActor(0, 1, 1.1234);
        var expected = 1.1234 - 0.5;
        EnableExecutorRollers();
        ExecutorTick();
        Assert.Equal((1, 1, expected), (actor.X, actor.Y, actor.Z));
        Assert.Null(actor.Movement.CurrentRef);
        var claims = _room.GetGameMap().Navigation!.Executor.Claims;
        Assert.Equal(TargetOccupancy.OffGraph, claims.OccupancyAt(5, 0));
        actor.MoveTo(2, 1);
        ExecutorTick();
        Assert.Contains("/mv 2,1,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((2, 1, 0d), (actor.X, actor.Y, actor.Z));
    }

    [Fact]
    public void ChainedRollersCarryEachCargoAndActorOnlyOncePerCycle()
    {
        ExecutorRoller(10, 0, 1);
        ExecutorRoller(11, 1, 1);
        var cargo = ExecutorFloor(12, 0, 1, z: 1, height: 0.5);
        var actor = ExecutorRollerActor(0, 1, 1.5);
        EnableExecutorRollers();
        ExecutorTick();
        Assert.Equal((1, 1, 1d), (cargo.GetX, cargo.GetY, cargo.GetZ));
        Assert.Equal((1, 1, 1.5), (actor.X, actor.Y, actor.Z));
        Assert.Equal(2, _client.Packets.Count(p => p.Header == ServerPacketHeader.SlideObjectBundleComposer));
        Assert.Equal("1.5", ExecutorAvatarSlide(actor).ToZ);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RollerSkipsActorsWithAnOutstandingPendingBatchEvenIfWalkingFlagIsCleared(bool walking)
    {
        ExecutorRoller(10, 0, 1);
        var actor = ExecutorRollerActor(0, 1, 0.5);
        actor.MoveTo(3, 1);
        ExecutorTick();
        Assert.Equal(1, actor.Movement.PendingCount);
        actor.IsWalking = walking;
        EnableExecutorRollers();
        ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.DoesNotContain(_client.Sent, header => header == ServerPacketHeader.SlideObjectBundleComposer);
        Assert.Contains("/mv 2,1,0/", ExecutorUpdate(actor).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RollerExecutionRulesRejectFloorLocksAndUnauthorizedGuildGates(bool guildGate)
    {
        ExecutorRoller(10, 0, 1);

        if (guildGate) {
            Add(11, 1, 1, type: InteractionType.GuildGate);
        }

        var actor = ExecutorRollerActor(0, 1, 0.5);

        if (!guildGate) {
            _room.GetGameMap().SetFloorStatus(1, 1, 0);
        }

        EnableExecutorRollers();
        ExecutorTick();
        Assert.Equal((0, 1, 0.5), (actor.X, actor.Y, actor.Z));
        Assert.DoesNotContain(_client.Sent, header => header == ServerPacketHeader.SlideObjectBundleComposer);
        Assert.Equal(TargetOccupancy.None, _room.GetGameMap().Navigation!.Executor.Claims.OccupancyAt(5, 0));
    }

    [Fact]
    public void RollerPreservesLegacyTransportWhenDormantDestinationClearanceDoesNotBlockCargo()
    {
        ExecutorRoller(10, 0, 1);
        ExecutorRoller(11, 1, 1);
        var cargo = ExecutorFloor(12, 1, 1, z: 1, height: 1);
        var actor = ExecutorRollerActor(0, 1, 0.5);
        EnableExecutorRollers();
        ExecutorTick();
        Assert.Equal((1, 1, 0.5), (actor.X, actor.Y, actor.Z));
        Assert.Single(_client.Packets, packet => ExecutorIsAvatarSlide(packet, actor.VirtualId));
        Assert.Equal((0, 1, 1, 1, 10u, "0.5", "0.5"), ExecutorAvatarSlide(actor));
        Assert.Equal((2, 1, 0.5), (cargo.GetX, cargo.GetY, cargo.GetZ));
        Assert.Equal((1, 1, 2, 1, "1", "0.5", 11u), ExecutorCargoSlide(cargo));
    }

    [Fact]
    public void LegacyRollerLoopCharacterizesDormantClearanceAndRetainedCarryZ()
    {
        ExecutorRoller(10, 0, 1);
        ExecutorRoller(11, 1, 1);
        var cargo = ExecutorFloor(12, 1, 1, z: 1, height: 1);
        var actor = Viewer(0, 1);
        actor.InternalRoomId = actor.VirtualId;
        actor.SetPos(0, 1, 0.5);
        _room.GetGameMap().AddUserToMap(actor, new(0, 1));
        Assert.Null(_room.GetGameMap().Navigation);
        EnableExecutorRollers();
        ExecutorTick();
        Assert.Equal((1, 1, 0.5), (actor.X, actor.Y, actor.Z));
        Assert.Single(_client.Packets, packet => ExecutorIsAvatarSlide(packet, actor.VirtualId));
        Assert.Equal((0, 1, 1, 1, 10u, "0.5", "0.5"), ExecutorAvatarSlide(actor));
        Assert.Equal((2, 1, 0.5), (cargo.GetX, cargo.GetY, cargo.GetZ));
        Assert.Equal((1, 1, 2, 1, "1", "0.5", 11u), ExecutorCargoSlide(cargo));
    }

    [Fact]
    public void RollerIgnoresStepHeightAndKeepsCarriedZInsteadOfSnappingToTargetTop()
    {
        ExecutorRoller(10, 0, 1);
        ExecutorFloor(11, 1, 1, height: 20);
        var actor = ExecutorRollerActor(0, 1, 0.5);
        EnableExecutorRollers();
        ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Null(actor.Movement.CurrentRef);
        Assert.Equal(20, _room.GetGameMap().Navigation!.Grid.WalkZ[5]);
        Assert.Equal("0", ExecutorAvatarSlide(actor).ToZ);
    }

    [Fact]
    public void RollerClaimIsHeldThroughLandingCallbacksAndReleasedAtUserPhaseEnd()
    {
        ExecutorRoller(10, 0, 1);
        ExecutorFloor(11, 1, 1);
        var observed = new List<TargetOccupancy>();
        var actor = ExecutorRollerActor(0, 1, 0.5);
        ExecutorObserveRollerClaim(observed);
        EnableExecutorRollers();
        ExecutorTick();
        Assert.Equal(new[] { TargetOccupancy.Stationary | TargetOccupancy.RollerClaim }, observed);
        var occupancy = _room.GetGameMap().Navigation!.Executor.Claims.OccupancyAt(5, 0);
        Assert.Equal(TargetOccupancy.Stationary, occupancy);
        Assert.False(actor.HasStatus("mv"));
    }

    private Item ExecutorRoller(uint id, int x, int y)
    {
        var roller = Furni(id, InteractionType.Roller, WiredBoxType.None);
        roller.Definition.Walkable = true;
        roller.Definition.Height = 0.5;
        roller.Definition.Width = roller.Definition.Length = 1;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, roller, x, y, 2, true, false, false));

        return roller;
    }

    private RoomUser ExecutorRollerActor(int x, int y, double z)
    {
        var actor = ExecutorActor(x, y);
        actor.SetPos(x, y, z);
        ExecutorTick();
        Assert.Equal((x, y, z), (actor.X, actor.Y, actor.Z));

        return actor;
    }

    private void EnableExecutorRollers()
    {
        var handler = _room.GetRoomItemHandler();
        handler.SetSpeed(0);
        handler.GotRollers = true;
    }

    private (int FromX, int FromY, int ToX, int ToY, uint Roller, string FromZ, string ToZ)
        ExecutorAvatarSlide(RoomUser actor)
    {
        foreach (var sent in _client.Packets.Where(p => p.Header == ServerPacketHeader.SlideObjectBundleComposer)) {
            var packet = new FlashIncomingPacket { Buffer = sent.Body.ToArray() };
            var fromX = packet.ReadInt();
            var fromY = packet.ReadInt();
            var toX = packet.ReadInt();
            var toY = packet.ReadInt();

            if (packet.ReadInt() != 0) {
                continue;
            }

            var roller = packet.ReadUInt();
            Assert.Equal(2, packet.ReadInt());

            if (packet.ReadInt() != actor.VirtualId) {
                continue;
            }

            return (fromX, fromY, toX, toY, roller, packet.ReadString(), packet.ReadString());
        }

        throw new InvalidOperationException("No roller slide for actor");
    }

    private bool ExecutorHasUpdate(RoomUser actor)
    {
        foreach (var sent in _client.Packets.Where(p => p.Header == ServerPacketHeader.UserUpdateComposer)) {
            var body = new FlashIncomingPacket { Buffer = sent.Body.ToArray() };
            var count = body.ReadInt();

            for (var i = 0; i < count; i++) {
                var id = body.ReadInt();
                body.ReadInt();
                body.ReadInt();
                body.ReadString();
                body.ReadInt();
                body.ReadInt();
                body.ReadString();

                if (id == actor.VirtualId) {
                    return true;
                }
            }
        }

        return false;
    }

    private void ExecutorObserveRollerClaim(List<TargetOccupancy> observed)
    {
        var item = Furni(900, InteractionType.WiredTrigger, WiredBoxType.TriggerWalkOnFurni);
        item.Definition.Height = 0;
        item.Definition.Width = item.Definition.Length = 1;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, item, 3, 3, 0, true, false, false));
        Assert.True(_room.GetWired().AddBox(new RollerClaimObserver(_room, item, observed)));
    }

    private static bool ExecutorIsAvatarSlide((uint Header, byte[] Body) sent, int virtualId)
    {
        if (sent.Header != ServerPacketHeader.SlideObjectBundleComposer) {
            return false;
        }

        var packet = new FlashIncomingPacket { Buffer = sent.Body.ToArray() };

        for (var field = 0; field < 4; field++) {
            packet.ReadInt();
        }

        if (packet.ReadInt() != 0) {
            return false;
        }

        packet.ReadUInt();
        packet.ReadInt();

        return packet.ReadInt() == virtualId;
    }

    private (int FromX, int FromY, int ToX, int ToY, string FromZ, string ToZ, uint Roller)
        ExecutorCargoSlide(Item cargo)
    {
        foreach (var sent in _client.Packets.Where(p => p.Header == ServerPacketHeader.SlideObjectBundleComposer)) {
            var packet = new FlashIncomingPacket { Buffer = sent.Body.ToArray() };
            var fromX = packet.ReadInt();
            var fromY = packet.ReadInt();
            var toX = packet.ReadInt();
            var toY = packet.ReadInt();

            if (packet.ReadInt() != 1 || packet.ReadUInt() != cargo.Id) {
                continue;
            }

            return (fromX, fromY, toX, toY, packet.ReadString(), packet.ReadString(), packet.ReadUInt());
        }

        throw new InvalidOperationException("No roller slide for cargo");
    }

    private sealed class RollerClaimObserver(Room room, Item item, List<TargetOccupancy> observed) : IWiredItem
    {
        public Room Instance { get; set; } = room;
        public Item Item { get; set; } = item;
        public WiredBoxType Type => WiredBoxType.TriggerWalkOnFurni;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] arguments)
        {
            if (((Item)arguments[1]).Id == 11) {
                observed.Add(Instance.GetGameMap().Navigation!.Executor.Claims.OccupancyAt(5, 0));
            }

            return false;
        }
    }
}
