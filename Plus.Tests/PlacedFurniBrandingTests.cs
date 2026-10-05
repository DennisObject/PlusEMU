using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.Communication.Packets.Outgoing;
using Xunit;

namespace Plus.Tests;

/// <summary>Branding saves through the real metadata service into the real placement path; only the item store is faked.</summary>
public partial class PlacedFurniRoomTests
{
    [Fact]
    public void BrandingIsWrittenBeforePublicationAndTheSnapshotIsAttachedAfterCommit()
    {
        var item = PlacedBranding(20, InteractionType.Background);
        Recipient();
        item.ExtraData = new MapDataFormat(new() { ["old"] = "value" });
        var store = new BrandingStore(() =>
        {
            Assert.Equal("old\tvalue", item.ExtraData.Serialize());
            Assert.False(MovedItems().ContainsKey(20));
            Assert.Empty(ObjectUpdates());
        });

        new RoomItemMetadataService(store, null!).SetBranding(_client, new(20, Pairs("a", "1", "b", "2")));

        Assert.Equal((1, 20u, RoomId, "state\t0\na\t1\nb\t2"), (store.Writes, store.ItemId, store.RoomId, store.Data));
        Assert.Equal("state\t0\na\t1\nb\t2", item.ExtraData.Serialize());
        var update = Assert.Single(ObjectUpdates());
        Assert.True(update.Body.AsSpan().IndexOf(Encode("state", "0", "a", "1", "b", "2")) >= 0);
        Assert.True(MovedItems().ContainsKey(20));
    }

    [Fact]
    public void BlockedPlacementNeverWritesOrChangesTheModel()
    {
        var item = PlacedBranding(21, InteractionType.Background);
        Recipient();
        item.ExtraData = new MapDataFormat(new() { ["old"] = "value" });
        item.SetState(99, 99, 0, new());
        var store = new BrandingStore();

        new RoomItemMetadataService(store, null!).SetBranding(_client, new(21, Pairs("a", "1")));

        Assert.Equal(0, store.Writes);
        Assert.Equal("old\tvalue", item.ExtraData.Serialize());
        Assert.Empty(ObjectUpdates());
        Assert.Empty(_client.Sent);
        Assert.False(MovedItems().ContainsKey(21));
    }

    [Fact]
    public void FailingStoreLeavesStateGeometryAndPublicationUnchanged()
    {
        var item = PlacedBranding(22, InteractionType.Background);
        Recipient();
        item.ExtraData = new MapDataFormat(new() { ["old"] = "value" });
        var before = (item.GetX, item.GetY, item.GetZ, item.Rotation);
        var store = new BrandingStore { Fail = true };

        Assert.Throws<InvalidOperationException>(() => new RoomItemMetadataService(store, null!).SetBranding(_client, new(22, Pairs("a", "1"))));

        Assert.Equal(1, store.Writes);
        Assert.Equal("old\tvalue", item.ExtraData.Serialize());
        Assert.Equal(before, (item.GetX, item.GetY, item.GetZ, item.Rotation));
        Assert.Empty(ObjectUpdates());
        Assert.Empty(_client.Sent);
        Assert.False(MovedItems().ContainsKey(22));
    }

    [Theory]
    [InlineData("right")]
    [InlineData("permission")]
    [InlineData("temporary")]
    [InlineData("image")]
    [InlineData("missing-map")]
    public void DeniedBrandingNeverReachesTheStoreOrTheModel(string denial)
    {
        var item = denial == "temporary" ? FloorTemporary(23) : PlacedBranding(23, InteractionType.Background);
        Recipient();
        item.ExtraData = new MapDataFormat(new() { ["old"] = "value" });
        if (denial == "right") _room.OwnerName = "someone-else";
        if (denial == "permission") _client.GetHabbo().Access = UserAccess.Empty;
        var values = denial switch
        {
            "image" => Pairs("url", "https://example.invalid/x.png"),
            "missing-map" => null,
            _ => Pairs("a", "1"),
        };
        var store = new BrandingStore();

        new RoomItemMetadataService(store, null!).SetBranding(_client, new(23, values));

        Assert.Equal(0, store.Writes);
        Assert.Equal("old\tvalue", item.ExtraData.Serialize());
        Assert.Empty(ObjectUpdates());
    }

    [Theory]
    [InlineData(InteractionType.Gate)]
    [InlineData(InteractionType.FxProvider)]
    public void IdOnlyAndNonBackgroundFramesKeepThePlacementOnlyRepublish(InteractionType type)
    {
        var item = PlacedBranding(24, type);
        Recipient();
        item.ExtraData = new MapDataFormat(new() { ["old"] = "value" });
        var store = new BrandingStore();
        var service = new RoomItemMetadataService(store, null!);

        service.SetBranding(_client, new(24, null));
        Assert.Single(ObjectUpdates());
        service.SetBranding(_client, new(24, Pairs("a", "1")));

        Assert.Equal(0, store.Writes);
        Assert.Equal("old\tvalue", item.ExtraData.Serialize());
        Assert.Equal(2, ObjectUpdates().Count);
        Assert.True(MovedItems().ContainsKey(24));
    }

    private Item PlacedBranding(uint id, InteractionType type)
    {
        _client.GetHabbo().Access = Branding();
        var item = Furni(id, type, WiredBoxType.None);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, 1, 1, 0, true, false, false));
        MovedItems().Clear();
        _client.Sent.Clear();
        _client.Packets.Clear();
        return item;
    }

    // A registered room user bound to the fixture client receives room broadcasts like a real occupant.
    private RoomUser Recipient()
    {
        var user = new RoomUser(7, RoomId, 1, _room);
        typeof(RoomUser).GetField("_mClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(user, _client);
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        users.TryAdd(1, user);
        return user;
    }

    private List<(uint Header, byte[] Body)> ObjectUpdates() =>
        _client.Packets.Where(packet => packet.Header == ServerPacketHeader.ObjectUpdateComposer).ToList();

    private static ImmutableArray<string>? Pairs(params string[] values) => ImmutableArray.Create(values);

    // Flash string framing: a big-endian UInt16 length followed by UTF-8 bytes, in key/value order.
    private static byte[] Encode(params string[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var length = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)bytes.Length));
            stream.Write(length);
            stream.Write(bytes);
        }
        return stream.ToArray();
    }

    private Item FloorTemporary(uint id)
    {
        _client.GetHabbo().Access = Branding();
        var item = new Item { Id = id, RoomId = _room.Id, OwnerId = 7, IsTemporary = true, Definition = new ItemDefinition { InteractionType = InteractionType.Background, Type = ItemType.Floor } };
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomItemHandler())!;
        floor[id] = item;
        return item;
    }

    private ConcurrentDictionary<uint, Item> MovedItems() =>
        (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_movedItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomItemHandler())!;

    private static UserAccess Branding() =>
        UserAccess.Create(Array.Empty<RoleAssignment>(), [new UserPermissionOverride(PermissionKeys.RoomItemSaveBrandingItems, false)]);

    private sealed class BrandingStore(Action? beforeWrite = null) : IRoomItemMetadataStore
    {
        public bool Fail { get; init; }
        public int Writes { get; private set; }
        public uint ItemId { get; private set; }
        public uint RoomId { get; private set; }
        public string? Data { get; private set; }
        public void SetBrandingData(uint itemId, uint roomId, string data)
        {
            beforeWrite?.Invoke();
            Writes++;
            (ItemId, RoomId, Data) = (itemId, roomId, data);
            if (Fail) throw new InvalidOperationException("forced failure");
        }
        public void SetMannequinData(uint itemId, uint roomId, string data) => throw new NotSupportedException();
        public void SetToner(uint itemId, uint roomId, int hue, int saturation, int lightness) => throw new NotSupportedException();
        public Plus.HabboHotel.Items.Data.Moodlight.MoodlightRecord? LoadMoodlight(uint itemId) => throw new NotSupportedException();
        public void SetMoodlightEnabled(uint itemId, uint roomId, bool enabled) => throw new NotSupportedException();
        public void UpdateMoodlightPreset(uint itemId, uint roomId, int preset, string value) => throw new NotSupportedException();
        public Plus.HabboHotel.Items.Data.Toner.TonerRecord? LoadToner(uint itemId) => throw new NotSupportedException();
    }
}
