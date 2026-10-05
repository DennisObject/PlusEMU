using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class RoomInteractionServiceTests
{
    [Fact]
    public void RatingRejectsInvalidOwnerAndDuplicateRequests()
    {
        var store = new RecordingStore();
        var service = new RoomInteractionService(store);
        var (room, owner, _) = Context("owner");
        service.Rate(room, owner, 2);
        service.Rate(room, owner, 1);
        var (otherRoom, voter, _) = Context("owner", "voter");
        voter.GetHabbo().RatedRooms.Add(otherRoom.RoomId);
        service.Rate(otherRoom, voter, 1);

        Assert.Equal(0, store.Ratings);
        Assert.Equal(0, room.Score);
        Assert.Equal(0, otherRoom.Score);
    }

    [Fact]
    public void SuccessfulRatingPublishesCommittedScoreOnce()
    {
        var (room, client, sent) = Context("owner", "voter");
        var store = new RecordingStore { ResultingScore = 42 };
        var service = new RoomInteractionService(store);

        service.Rate(room, client, -1);
        service.Rate(room, client, -1);

        Assert.Equal(1, store.Ratings);
        Assert.Equal(42, room.Score);
        Assert.Contains(room.RoomId, client.GetHabbo().RatedRooms);
        Assert.Single(sent);
    }

    [Fact]
    public void RatingFailureDoesNotPublishMemoryOrPacket()
    {
        var (room, client, sent) = Context("owner", "voter");
        var store = new RecordingStore { Fail = true };

        Assert.Throws<InvalidOperationException>(() => new RoomInteractionService(store).Rate(room, client, 1));

        Assert.Equal(0, room.Score);
        Assert.Empty(client.GetHabbo().RatedRooms);
        Assert.Empty(sent);
    }

    [Fact]
    public void StickyWrongTypeAndPersistenceFailureLeaveFurnitureInRoom()
    {
        var (room, client, _) = Context("owner");
        var item = AddItem(room, InteractionType.Gate);
        var store = new RecordingStore();
        var service = new RoomInteractionService(store);
        service.DeleteSticky(room, client, item.Id);
        item.Definition.InteractionType = InteractionType.Postit;
        store.Fail = true;

        Assert.Throws<InvalidOperationException>(() => service.DeleteSticky(room, client, item.Id));
        Assert.Same(item, room.GetRoomItemHandler().GetItem(item.Id));
        Assert.Equal(1, store.Deletions);
    }

    private static (Room Room, GameClient Client, List<(uint Header, byte[] Payload)> Sent) Context(string owner, string username = "owner")
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 9; room.OwnerName = owner; room.Type = "private"; room.UsersWithRights = [];
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomItemHandling(room));
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room));
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = username == owner ? 1 : 2, Username = username, CurrentRoom = room });
        return (room, client, sent);
    }

    private static Item AddItem(Room room, InteractionType type)
    {
        var item = new Item { Id = 7, RoomId = room.Id, OwnerId = 1, Definition = new() { InteractionType = type } };
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;
        floor[item.Id] = item;
        return item;
    }

    private sealed class RecordingStore : IRoomInteractionStore
    {
        public int ResultingScore { get; init; }
        public bool Fail { get; set; }
        public int Ratings { get; private set; }
        public int Deletions { get; private set; }
        public int AddRating(uint roomId, int rating) { Ratings++; if (Fail) throw new InvalidOperationException("forced"); return ResultingScore; }
        public void DeleteSticky(uint itemId, uint roomId) { Deletions++; if (Fail) throw new InvalidOperationException("forced"); }
    }
}
