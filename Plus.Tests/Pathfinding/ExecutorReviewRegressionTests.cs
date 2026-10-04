using System.Collections.Concurrent;
using System.Drawing;
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
    [Theory]
    [InlineData("furniture", 1.75)]
    [InlineData("seat", .25)]
    [InlineData("magic", .5)]
    [InlineData("void", 0)]
    public void PrivilegedRoutesUseLegacyIdentityAndHeightWithSeparatePhysicalSupport(string terrain, double z)
    {
        ReviewOverrideTerrain(terrain);
        var actor = ExecutorActor(0, 1); actor.AllowOverride = true;
        actor.MoveTo(2, 1); ExecutorTick();
        Assert.Equal($"1,1,{Plus.Utilities.TextHandling.GetString(z)}", actor.Statusses["mv"]);
        ExecutorTick();
        Assert.Equal((1, 1, z), (actor.X, actor.Y, actor.Z));
        if (terrain != "void")
            Assert.Equal(_room.GetGameMap().Navigation!.Grid.Reference(5), actor.Movement.CurrentRef);
        else Assert.Null(actor.Movement.CurrentRef);
        Assert.False(actor.HasStatus("sit")); Assert.False(actor.HasStatus("lay"));
        ExecutorTick(); Assert.Equal((2, 1, 0d), (actor.X, actor.Y, actor.Z));
    }

    [Fact]
    public void PrivilegedMountedWalkMagicUsesTheActualSupportWithoutARiderOffset()
    {
        Add(10, 1, 1, z: .5, type: InteractionType.WalkMagicTile);
        var actor = ExecutorActor(0, 1);
        var horse = ExecutorAdditionalBot(0, 1, 2); ExecutorTick();
        EstablishExternalMountedGroup(actor, horse);
        actor.AllowOverride = true; actor.MoveTo(2, 1); ExecutorTick();
        Assert.Equal("1,1,0.5", actor.Statusses["mv"]);
        Assert.Equal("1,1,0.5", horse.Statusses["mv"]);
        ExecutorTick();
        Assert.Equal((1, 1, .5), (actor.X, actor.Y, actor.Z));
        Assert.Equal((1, 1, .5), (horse.X, horse.Y, horse.Z));
        Assert.Equal(SurfaceKind.WalkMagic, actor.Movement.CurrentRef!.Value.Kind);
        Assert.Equal(actor.Movement.CurrentRef, horse.Movement.CurrentRef);
    }

    private void ReviewOverrideTerrain(string terrain)
    {
        if (terrain == "furniture") ExecutorFloor(10, 1, 1, z: .25, height: 1.5);
        if (terrain == "seat") Add(10, 1, 1, z: .25, height: .5, seat: true);
        if (terrain == "magic")
        { Add(10, 1, 1, height: 2); Add(11, 1, 1, z: .5, type: InteractionType.WalkMagicTile); }
        if (terrain != "void") return;
        Set("_gamemap", new Gamemap(_room, new RoomModel("override-void", 0, 0, 0, 0,
            "0000\r0x00\r0000\r0000", false, 0, false)));
        _room.GetGameMap().GenerateMaps();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WalkOffTerminalCallbacksLeaveNoSpeculativeMapMembership(bool remove)
    {
        var origin = ExecutorFloor(10, 0, 1); ExecutorFloor(11, 1, 1);
        var actor = ExecutorActor(0, 1);
        ReviewObserveWalkOff((user, item) =>
        {
            if (item != origin) return;
            var navigation = _room.GetGameMap().Navigation!;
            if (remove) navigation.Remove(user);
            else navigation.ForcePlace(user, 3, 2, 0, ForceResolution.ExactZ);
        });
        actor.MoveTo(1, 1); ExecutorTick(); ExecutorTick();
        Assert.Equal(remove ? NavState.Removing : NavState.Active, actor.Movement.State);
        ReviewAssertMapMembership(actor, remove ? null : new Point(3, 2));
        if (!remove) Assert.Equal((3, 2, 0d), (actor.X, actor.Y, actor.Z));
        Assert.False(actor.HasStatus("mv"));
    }

    [Theory]
    [InlineData(false, 1.5)]
    [InlineData(true, 0)]
    public void WalkOffGeometryChangesResolveTheLatestDestinationSupport(bool remove, double expectedZ)
    {
        var origin = ExecutorFloor(10, 0, 1);
        var destination = ExecutorFloor(11, 1, 1, z: .5, height: .5);
        var actor = ExecutorActor(0, 1);
        ReviewObserveWalkOff((_, item) =>
        {
            if (item != origin) return;
            if (remove) _room.GetRoomItemHandler().RemoveFurniture(null!, destination.Id);
            else destination.GetZ = 1;
            _room.GetGameMap().Navigation!.ApplyDirty();
        });
        actor.MoveTo(1, 1); ExecutorTick(); ExecutorTick();
        var grid = _room.GetGameMap().Navigation!.Grid;
        Assert.Equal((1, 1, expectedZ), (actor.X, actor.Y, actor.Z));
        Assert.Equal(grid.Reference(5), actor.Movement.CurrentRef);
        Assert.Equal(expectedZ, actor.Movement.SupportZ);
        ReviewAssertMapMembership(actor, new(1, 1));
    }

    [Theory]
    [InlineData("sit")]
    [InlineData("lay")]
    public void UnrelatedGeometryPublicationPreservesManualPosture(string posture)
    {
        var remote = ExecutorFloor(10, 3, 2);
        var actor = ExecutorActor(0, 1);
        ReviewManualPosture(actor, posture);
        var version = actor.Movement.BoundVersion;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(remote, 2, 2, 0));
        ExecutorTick();
        Assert.True(_room.GetGameMap().Navigation!.Grid.Version > version);
        Assert.Equal("0.5", actor.Statusses[posture]);
        Assert.Equal(posture == "sit", actor.IsSitting); Assert.Equal(posture == "lay", actor.IsLying);
        Assert.Equal((0, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(_room.GetGameMap().Navigation!.Grid.Version, actor.Movement.BoundVersion);
        Assert.Contains($"/{posture} 0.5/", ExecutorUpdate(actor).Status);
    }

    [Theory]
    [InlineData(true)]
    public void CurvedSuperfastBatchKeepsLegacyOriginToEndpointFacing(bool v2)
    {
        var actor = v2 ? ExecutorActor(0, 1) : Viewer(0, 1);
        if (!v2) { actor.UserId = 7; _room.GetGameMap().AddUserToMap(actor, actor.Coordinate); }
        ReviewCurvedRoute(actor, v2); ExecutorTick();
        Assert.Equal("1,1,0", actor.Statusses["mv"]);
        Assert.Equal(2, actor.RotHead); Assert.Equal(2, actor.RotBody);
        Assert.Equal((2, 2), ReviewPacketFacing(actor));
        ExecutorTick(); Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Equal((2, 2), ReviewPacketFacing(actor));
    }

    private void ReviewCurvedRoute(RoomUser actor, bool v2)
    {
        actor.SuperFastWalking = true; actor.GoalX = 1; actor.GoalY = 1;
        actor.IsWalking = true; actor.PathRecalcNeeded = false;
        if (!v2) { actor.Path = [new(1, 1), new(1, 2), new(0, 2), new(0, 1)]; actor.PathStep = 1; return; }
        var grid = _room.GetGameMap().Navigation!.Grid;
        var state = actor.Movement; state.HasIntent = true; state.Origin = MoveOrigin.User;
        state.Route.Count = 3; state.Route.GridVersion = grid.Version;
        state.Route.Set(0, grid.Reference(8)); state.Route.Set(1, grid.Reference(9)); state.Route.Set(2, grid.Reference(5));
        state.Route.GoalSurface = grid.Reference(5);
    }

    private static void ReviewManualPosture(RoomUser actor, string posture)
    {
        actor.SetStatus(posture, "0.5");
        actor.IsSitting = posture == "sit"; actor.IsLying = posture == "lay";
        // Manual posture commands also schedule the changed status for serialization.
        actor.UpdateNeeded = true;
    }

    private (int Head, int Body) ReviewPacketFacing(RoomUser actor)
    {
        var sent = _client.Packets.Last(packet => packet.Header == ServerPacketHeader.UserUpdateComposer);
        var packet = new FlashIncomingPacket { Buffer = sent.Body.ToArray() };
        var count = packet.ReadInt();
        for (var index = 0; index < count; index++)
        {
            var id = packet.ReadInt(); packet.ReadInt(); packet.ReadInt(); packet.ReadString();
            var head = packet.ReadInt(); var body = packet.ReadInt(); packet.ReadString();
            if (id == actor.VirtualId) return (head, body);
        }
        throw new InvalidOperationException("actor packet missing");
    }

    private void ReviewAssertMapMembership(RoomUser actor, Point? registered)
    {
        var map = _room.GetGameMap();
        for (var y = 0; y < map.StaticModel.MapSizeY; y++)
            for (var x = 0; x < map.StaticModel.MapSizeX; x++)
                Assert.Equal(registered == new Point(x, y) ? 1 : 0,
                    map.GetRoomUsers(new(x, y)).Count(member => ReferenceEquals(member, actor)));
    }

    private void ReviewObserveWalkOff(Action<RoomUser, Item> action)
    {
        var item = Furni(902, InteractionType.WiredTrigger, WiredBoxType.TriggerWalkOffFurni);
        item.Definition.Height = 0; item.Definition.Width = item.Definition.Length = 1;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, item, 3, 3, 0, true, false, false));
        Assert.True(_room.GetWired().AddBox(new ReviewWalkOffObserver(_room, item, action)));
    }

    private sealed class ReviewWalkOffObserver(Room room, Item item, Action<RoomUser, Item> action) : IWiredItem
    {
        public Room Instance { get; set; } = room;
        public Item Item { get; set; } = item;
        public WiredBoxType Type => WiredBoxType.TriggerWalkOffFurni;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] arguments)
        {
            var actor = Instance.GetRoomUserManager().GetRoomUserByHabbo(((Plus.HabboHotel.Users.Habbo)arguments[0]).Id);
            action(actor, (Item)arguments[1]); return false;
        }
    }
}
