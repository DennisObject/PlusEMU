using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.IO;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Rooms.Tests;

public class FurnitureListPacketTests
{
    [Fact]
    public void FloorListIncludesEveryOwnerAndDefaultExtra()
    {
        var room = RoomFixture.Create();
        var items = new[] { CreateItem(101, room.OwnerId), CreateItem(102, 20) };
        var reader = Compose(new ObjectsComposer(items, room));

        var owners = reader.ReadOwners();
        Assert.Equal(2, owners.Count);
        Assert.Equal(room.OwnerName, owners[room.OwnerId]);
        Assert.Equal(string.Empty, owners[20]);
        Assert.Equal(2, reader.ReadInt());
        foreach (var item in items) AssertFloorItem(reader, item);
        reader.AssertEnd();
    }

    [Theory]
    [InlineData(InteractionType.None)]
    [InlineData(InteractionType.MusicDisc)]
    public void FloorListWithSingleRoomOwnerStillParses(InteractionType interaction)
    {
        var room = RoomFixture.Create();
        var item = CreateItem(101, room.OwnerId);
        item.Definition.InteractionType = interaction;
        var reader = Compose(new ObjectsComposer(new[] { item }, room));

        var owner = Assert.Single(reader.ReadOwners());
        Assert.Equal(room.OwnerId, owner.Key);
        Assert.Equal(room.OwnerName, owner.Value);
        Assert.Equal(1, reader.ReadInt());
        AssertFloorItem(reader, item);
        reader.AssertEnd();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerMapDeduplicatesOwnersAndResolvesRoomUsers(bool wall)
    {
        var room = RoomFixture.Create();
        RoomFixture.AddUser(room, 20, "Furniture owner ✓");
        var items = new[] { CreateItem(101, 20, wall), CreateItem(102, 20, wall) };
        var reader = Compose(wall ? new ItemsComposer(items, room) : new ObjectsComposer(items, room));

        var owner = Assert.Single(reader.ReadOwners());
        Assert.Equal(20, owner.Key);
        Assert.Equal("Furniture owner ✓", owner.Value);
        Assert.Equal(2, reader.ReadInt());
        foreach (var item in items)
        {
            if (wall) AssertWallItem(reader, item, 20);
            else AssertFloorItem(reader, item);
        }
        reader.AssertEnd();
    }

    [Theory]
    [InlineData(20, 20, "")]
    [InlineData(0, 10, "Room owner")]
    public void WallListOwnerMapMatchesTrailer(int itemOwner, int expectedOwner, string expectedName)
    {
        var room = RoomFixture.Create();
        var item = CreateItem(101, itemOwner, true);
        var reader = Compose(new ItemsComposer(new[] { item }, room));

        var owner = Assert.Single(reader.ReadOwners());
        Assert.Equal(expectedOwner, owner.Key);
        Assert.Equal(expectedName, owner.Value);
        Assert.Equal(1, reader.ReadInt());
        AssertWallItem(reader, item, expectedOwner);
        reader.AssertEnd();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyListHasNoOwners(bool wall)
    {
        var room = RoomFixture.Create();
        var reader = Compose(wall ? new ItemsComposer(Array.Empty<Item>(), room) : new ObjectsComposer(Array.Empty<Item>(), room));

        Assert.Empty(reader.ReadOwners());
        Assert.Equal(0, reader.ReadInt());
        reader.AssertEnd();
    }

    [Theory]
    [InlineData("Recipient\u0005Message\u000510\u0005123\u0005456\u00053\u00057", 7003)]
    [InlineData("legacy gift", 1)]
    [InlineData("Recipient\u0005Message\u000510\u0005123\u0005456\u0005bad\u00057", 1)]
    public void GiftExtraUsesExistingColorAndRibbonWhenAvailable(string data, int extra)
    {
        var room = RoomFixture.Create();
        var item = CreateItem(101, room.OwnerId);
        item.Definition.InteractionType = InteractionType.Gift;
        item.ExtraData = new LegacyDataFormat { Data = data };
        var reader = Compose(new ObjectsComposer(new[] { item }, room));

        Assert.Single(reader.ReadOwners());
        Assert.Equal(1, reader.ReadInt());
        AssertFloorItem(reader, item, extra);
        reader.AssertEnd();
    }

    private static Item CreateItem(uint id, int userId, bool wall = false) => new()
    {
        Id = id,
        UserId = userId,
        Definition = new ItemDefinition
        {
            SpriteId = 345,
            Type = wall ? ItemType.Wall : ItemType.Floor,
            Height = 1.25,
            Modes = 2
        },
        GetX = 2,
        GetY = 3,
        GetZ = 4.5,
        Rotation = 6,
        WallCoordinates = ":w=1,2 l=3,4 r",
        ExtraData = new LegacyDataFormat { Data = "2" }
    };

    private static PacketReader Compose(IServerPacket composer)
    {
        using var stream = (RecyclableMemoryStream)new RecyclableMemoryStreamManager().GetStream();
        var packet = new FlashPacketFactory().CreateOutgoingPacket(stream);
        composer.Compose(packet);
        // FlashPacketFactory reserves six bytes for the transport header.
        return new PacketReader(stream.ToArray().AsMemory(6));
    }

    private static void AssertFloorItem(PacketReader reader, Item item, int extra = 1)
    {
        Assert.Equal((int)item.Id, reader.ReadInt());
        Assert.Equal(item.Definition.SpriteId, reader.ReadInt());
        Assert.Equal(item.GetX, reader.ReadInt());
        Assert.Equal(item.GetY, reader.ReadInt());
        Assert.Equal(item.Rotation, reader.ReadInt());
        Assert.Equal("4.5", reader.ReadString());
        Assert.Equal("1.25", reader.ReadString());
        Assert.Equal(extra, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt()); // Nitro's legacy object-data type.
        Assert.Equal(item.LegacyDataString, reader.ReadString());
        Assert.Equal(-1, reader.ReadInt());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(item.UserId, reader.ReadInt());
    }

    private static void AssertWallItem(PacketReader reader, Item item, int userId)
    {
        Assert.Equal(item.Id.ToString(), reader.ReadString());
        Assert.Equal(item.Definition.SpriteId, reader.ReadInt());
        Assert.Equal(item.WallCoordinates, reader.ReadString());
        Assert.Equal(item.LegacyDataString, reader.ReadString());
        Assert.Equal(-1, reader.ReadInt());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(userId, reader.ReadInt());
    }

    private sealed class PacketReader
    {
        private ReadOnlyMemory<byte> _remaining;

        public PacketReader(ReadOnlyMemory<byte> buffer) => _remaining = buffer;

        public int ReadInt()
        {
            var value = BinaryPrimitives.ReadInt32BigEndian(_remaining.Span);
            _remaining = _remaining[4..];
            return value;
        }

        public string ReadString()
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(_remaining.Span);
            var value = Encoding.UTF8.GetString(_remaining.Span.Slice(2, length));
            _remaining = _remaining[(2 + length)..];
            return value;
        }

        public Dictionary<int, string> ReadOwners()
        {
            var count = ReadInt();
            var owners = new Dictionary<int, string>();
            for (var i = 0; i < count; i++) owners.Add(ReadInt(), ReadString());
            return owners;
        }

        public void AssertEnd() => Assert.True(_remaining.IsEmpty);
    }

    private static class RoomFixture
    {
        public static Room Create()
        {
            // Room's constructor loads maps, furniture, bots and rights from the database.
            // These composers only require owner metadata and the real room user manager.
            var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            room.OwnerId = 10;
            room.OwnerName = "Room owner";
            SetField(room, "_roomUserManager", new RoomUserManager(room));
            return room;
        }

        public static void AddUser(Room room, int id, string name)
        {
            var client = new FlashGameClient(null!, new FlashPacketFactory());
            client.SetHabbo(new Habbo { Id = id, Username = name });
            var user = new RoomUser(id, room.Id, 0, room);
            SetField(user, "_mClient", client);
            var manager = room.GetRoomUserManager();
            var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
                .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
            Assert.True(users.TryAdd(id, user));
        }

        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }
}
