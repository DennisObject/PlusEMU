using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Incoming.Moderation;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class ModeratorRoomInfoTests
{
    [Fact]
    public async Task HandlerDecodesRoomIdAndDelegates()
    {
        var service = new RecordingService();
        var (client, _) = HabbiconTestSupport.Client(new Habbo());
        await new GetModeratorRoomInfoEvent(service).Parse(client, HabbiconTestSupport.Incoming(-1));
        Assert.Equal(uint.MaxValue, service.RoomId);
        Assert.Same(client, service.Client);
    }

    [Fact]
    public void SnapshotWritesAllFieldsAndRetainsThemAfterRoomAndTagMutation()
    {
        var data = Data();
        var composer = new ModeratorRoomInfoComposer(ModeratorRoomInfoSnapshot.Capture(data, true));
        data.Id = 99;
        data.UsersNow = 0;
        data.OwnerId = 8;
        data.OwnerName = "changed";
        data.Name = "changed";
        data.Description = "changed";
        data.Tags[0] = "changed";
        data.Tags.Clear();
        for (var index = 0; index < 2; index++)
        {
            var packet = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(packet);
            Assert.Equal(new object[] { 42u, 3, true, 7, "owner", true, "room", "description", 2, "one", "two", false }, packet.Writes);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServiceCapturesOwnerPresenceFromTheLoadedRoom(bool ownerPresent)
    {
        var data = Data();
        var room = World(ownerPresent);
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        var service = new ModeratorRoomInfoService(new Loader(data), Rooms(room));

        service.Show(client, data.Id);

        var payload = Assert.Single(sent).Payload;
        var packet = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = payload };
        Assert.Equal(42u, packet.ReadUInt());
        Assert.Equal(3, packet.ReadInt());
        Assert.Equal(ownerPresent, packet.ReadBool());
        Assert.Equal(7, packet.ReadInt());
        Assert.Equal("owner", packet.ReadString());
        Assert.True(packet.ReadBool());
        Assert.Equal("room", packet.ReadString());
        Assert.Equal("description", packet.ReadString());
        Assert.Equal(2, packet.ReadInt());
        Assert.Equal("one", packet.ReadString());
        Assert.Equal("two", packet.ReadString());
        Assert.False(packet.ReadBool());
        Assert.False(packet.HasDataRemaining());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingMetadataOrUnloadedRoomDoesNotSend(bool metadataExists)
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        var service = new ModeratorRoomInfoService(new Loader(metadataExists ? Data() : null), Rooms(null));
        service.Show(client, 42);
        Assert.Empty(sent);
    }

    private static RoomData Data() => new()
    {
        Id = 42, UsersNow = 3, OwnerId = 7, OwnerName = "owner",
        Name = "room", Description = "description", Tags = ["one", "two"]
    };

    private static Room World(bool ownerPresent)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 42;
        var manager = new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, manager);
        if (ownerPresent)
        {
            var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 7, Username = "OWNER" });
            var user = new RoomUser(7, 42, 1, room, client, TestChatEmotions.Unused, TestRewardProgress.Unused);
            var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
                .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
            users.TryAdd(1, user);
        }
        return room;
    }

    private static IRoomManager Rooms(Room? room) => CatalogSnapshotTestSupport.Proxy<IRoomManager>((method, args) =>
    {
        Assert.Equal("TryGetRoom", method);
        Assert.Equal(42u, args[0]);
        args[1] = room;
        return room != null;
    });

    private sealed class Loader(RoomData? result) : IRoomDataLoader
    {
        public bool TryGetData(uint roomId, [NotNullWhen(true)] out RoomData? data)
        {
            Assert.Equal(42u, roomId);
            data = result;
            return data != null;
        }
        public List<RoomData> GetRoomsDataByOwnerSortByName(int ownerId) => throw new NotSupportedException();
    }

    private sealed class RecordingService : IModeratorRoomInfoService
    {
        public uint RoomId { get; private set; }
        public GameClient? Client { get; private set; }
        public void Show(GameClient session, uint roomId) { Client = session; RoomId = roomId; }
    }
}
