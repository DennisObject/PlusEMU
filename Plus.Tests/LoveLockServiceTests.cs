using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.GameClients;
using Xunit;

namespace Plus.Tests;

public sealed class LoveLockServiceTests
{
    [Fact]
    public void MissingParticipantsCancelSafelyWithoutPersistence()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 9;
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomItemHandling(room));
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room));
        var item = new Item { Id = 7, RoomId = room.Id, OwnerId = 1, Definition = new() { InteractionType = InteractionType.Lovelock }, InteractingUser = 1, InteractingUser2 = 2 };
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;
        floor[item.Id] = item;
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1, CurrentRoom = room });
        var store = new RecordingStore();

        new LoveLockService(store).Confirm(client, new(item.Id, true));

        Assert.Equal(0, item.InteractingUser);
        Assert.Equal(0, item.InteractingUser2);
        Assert.Equal(0, store.Writes);
        Assert.Single(sent);
    }

    [Fact]
    public void PersistenceFailureLeavesItemAndConfirmationStateUnchanged()
    {
        var room = TestRoom();
        var (oneClient, _) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "one", Look = "look1", CurrentRoom = room });
        var (twoClient, sent) = HabbiconTestSupport.Client(new Habbo { Id = 2, Username = "two", Look = "look2", CurrentRoom = room });
        var one = AddUser(room, oneClient, 1); var two = AddUser(room, twoClient, 2);
        one.LlPartner = 2;
        var item = AddItem(room, 1, 2);
        var store = new RecordingStore { Fail = true };

        Assert.Throws<InvalidOperationException>(() => new LoveLockService(store).Confirm(twoClient, new(item.Id, true)));

        Assert.Equal(1, item.InteractingUser);
        Assert.Equal(2, item.InteractingUser2);
        Assert.Equal(2, one.LlPartner);
        Assert.Equal(0, two.LlPartner);
        Assert.Empty(sent);
    }

    private static Room TestRoom()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)); room.Id = 9;
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomItemHandling(room));
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room));
        return room;
    }

    private static Item AddItem(Room room, int one, int two)
    {
        var item = new Item { Id = 7, RoomId = room.Id, OwnerId = 1, Definition = new() { InteractionType = InteractionType.Lovelock }, InteractingUser = one, InteractingUser2 = two };
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;
        floor[item.Id] = item; return item;
    }

    private static RoomUser AddUser(Room room, GameClient client, int id)
    {
        var user = new RoomUser(id, room.Id, id, room) { InternalRoomId = id };
        typeof(RoomUser).GetField("_mClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(user, client);
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomUserManager())!;
        users[id] = user; return user;
    }

    private sealed class RecordingStore : ILoveLockStore
    {
        public bool Fail { get; init; }
        public int Writes { get; private set; }
        public void Lock(uint itemId, uint roomId, string data) { Writes++; if (Fail) throw new InvalidOperationException("forced failure"); }
    }
}
