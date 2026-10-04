using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Speech;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void AdmissionServiceDeployBotPublishesPhysicalSpawnBeforeItsPacketAndQueuesMembership()
    {
        ExecutorActor(0, 1);
        var speech = new List<RandomSpeech>();
        var bot = new RoomBot(22, RoomId, "generic", "stand", "Spawn", "", "hr-100-1", 2, 2, 0, 2,
            0, 0, 3, 3, ref speech, "M", 0, 7, false, 20, false, 0);
        _client.Packets.Clear();
        var actor = _room.GetRoomUserManager().DeployBot(bot, null!);
        Assert.Equal((2, 2, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(NavState.PendingAdmission, actor.Movement.State);
        Assert.DoesNotContain(actor, _room.GetGameMap().GetRoomUsers(new(2, 2)));
        Assert.Contains(_client.Packets, p => p.Header == ServerPacketHeader.UsersComposer);
        actor.MoveTo(3, 2);
        ExecutorTick();
        Assert.Equal(NavState.Active, actor.Movement.State);
        Assert.Contains(actor, _room.GetGameMap().GetRoomUsers(new(2, 2)));
        Assert.Contains("/mv 3,2,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((3, 2), (actor.X, actor.Y));
    }

    [Fact]
    public void RebindServiceOffOwnerFurnitureHeightChangeWaitsForTheRoomOwner()
    {
        var tile = Add(10, 1, 1, z: .75, type: InteractionType.WalkMagicTile);
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1); ExecutorTick(); ExecutorTick();
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(tile, 1, 1, 2.125));
        Assert.Equal(.75, actor.Z);
        Assert.Equal(.75, actor.Movement.SupportZ);
        ExecutorTick();
        Assert.Equal(2.125, actor.Z);
        Assert.Equal(2.125, actor.Movement.SupportZ);
        Assert.Equal("2.125", ExecutorUpdate(actor).Z);
    }

    [Fact]
    public void ForcePlacementServiceOffOwnerTeleportToItemDefersTheWholeMembershipTransaction()
    {
        var destination = ExecutorFloor(10, 2, 2, z: .5);
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(3, 1); ExecutorTick();
        var revision = actor.Movement.LocationRevision;
        _room.GetGameMap().TeleportToItem(actor, destination);
        Assert.Equal((0, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Contains(actor, _room.GetGameMap().GetRoomUsers(new(0, 1)));
        Assert.DoesNotContain(actor, _room.GetGameMap().GetRoomUsers(new(2, 2)));
        ExecutorTick();
        Assert.Equal((2, 2, .5), (actor.X, actor.Y, actor.Z));
        Assert.Equal(revision + 1, actor.Movement.LocationRevision);
        Assert.False(actor.HasStatus("mv"));
        Assert.Contains(actor, _room.GetGameMap().GetRoomUsers(new(2, 2)));
    }
}
