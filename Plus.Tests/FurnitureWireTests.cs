using System.Data;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Xunit;

namespace Plus.Tests;

public class FurnitureWireTests
{
    [Fact]
    public void FloorItemExtraIsOneUnlessGiftOrDisc()
    {
        var plain = Floor(InteractionType.None, userId: 4);
        var gift = Floor(InteractionType.Gift, userId: 4, legacy: Gift("4"));
        var disc = Floor(InteractionType.MusicDisc, userId: 4, legacy: "a\nb\nc\nd\ne\nf\n77");
        var brokenDisc = Floor(InteractionType.MusicDisc, userId: 4, legacy: "no-song");

        Assert.Equal(1, Extra(plain));
        Assert.Equal(4004, Extra(gift));
        Assert.Equal(77, Extra(disc));
        Assert.Equal(1, Extra(brokenDisc));
    }

    [Fact]
    public void OwnerMapListsRoomOwnerFirstThenAscendingIds()
    {
        var packet = new RecordingPacket();
        var items = new[]
        {
            Floor(InteractionType.None, 9, username: ""),
            Floor(InteractionType.None, 5, username: ""),
            Floor(InteractionType.None, 5, username: "bob"),
            Floor(InteractionType.None, 2, username: "ignored")
        };

        RoomEngineSerializers.WriteOwnerMap(packet, items, roomOwnerId: 2, roomOwnerName: "owner");

        Assert.Equal(new object[]
        {
            3,
            2, "owner",
            5, "bob",
            9, ""
        }, packet.Writes);
    }

    [Fact]
    public void RoomRowKeepsOwnerNameAndGiftWrap()
    {
        var table = new DataTable();
        foreach (var column in new[] { "id", "user_id", "x", "y", "rot", "limited_number", "limited_stack" })
            table.Columns.Add(column, typeof(int));
        table.Columns.Add("z", typeof(double));
        table.Columns.Add("extra_data", typeof(string));
        table.Columns.Add("wall_pos", typeof(string));
        table.Columns.Add("username", typeof(string));
        table.Rows.Add(8, 5, 1, 2, 0, 0, 0, 0.5, Gift("4"), ":w=1,1 l=1,1 r", "bob");
        table.Rows.Add(9, 6, 0, 0, 0, 0, 0, 0, "", "", DBNull.Value);

        var definition = new ItemDefinition
        {
            SpriteId = 10,
            Height = 0.5,
            Modes = 1,
            InteractionType = InteractionType.Gift,
            ItemName = "",
            PublicName = "",
            VendingIds = new List<int>(),
            AdjustableHeights = new List<double>()
        };
        var gift = ItemLoader.ReadRoomItem(table.Rows[0], 3, definition);
        var missingUser = ItemLoader.ReadRoomItem(table.Rows[1], 3, definition);

        Assert.Equal("bob", gift.Username);
        Assert.Equal(4004, RoomEngineSerializers.FloorExtra(gift));
        Assert.Equal("", missingUser.Username);
        Assert.Equal(":w=1,1 l=1,1 r", gift.WallCoordinates);
    }

    [Fact]
    public void WallUpdateUsesPlacerId()
    {
        var item = Floor(InteractionType.Postit, userId: 7);
        item.Id = 15;
        item.OwnerId = 99;
        item.WallCoordinates = ":w=1,1 l=1,1 r";
        var packet = new RecordingPacket();

        new ItemUpdateComposer(item).Compose(packet);

        Assert.Equal(7, packet.Writes[^1]);
        Assert.DoesNotContain(99u, packet.Writes);
    }

    private static int Extra(Item item)
    {
        var packet = new RecordingPacket();
        packet.Serialize(item);
        return (int)packet.Writes[7];
    }

    private static string Gift(string wrap) => string.Join(((char)5).ToString(), "1", "hi", "2", "0", "0", "0", wrap);

    private static Item Floor(InteractionType type, int userId, string? legacy = null, string username = "")
    {
        var item = new Item
        {
            Id = 1,
            UserId = userId,
            Username = username,
            Definition = new ItemDefinition
            {
                SpriteId = 10,
                Height = 0.5,
                Modes = 1,
                InteractionType = type,
                ItemName = "",
                PublicName = "",
                VendingIds = new List<int>(),
                AdjustableHeights = new List<double>()
            }
        };
        if (legacy != null)
            item.ExtraData = new LegacyDataFormat { Data = legacy };
        return item;
    }

    private sealed class RecordingPacket : IOutgoingPacket
    {
        public List<object> Writes { get; } = new();
        public int MessageId { get; set; }
        public ReadOnlyMemory<byte> Buffer => ReadOnlyMemory<byte>.Empty;
        public void WriteByte(byte value) => Writes.Add(value);
        public void WriteShort(short value) => Writes.Add(value);
        public void WriteInt(int value) => Writes.Add(value);
        public void WriteInteger(int value) => Writes.Add(value);
        public void WriteUInt(uint value) => Writes.Add(value);
        public void WriteUInteger(uint value) => Writes.Add(value);
        public void WriteBool(bool value) => Writes.Add(value);
        public void WriteBoolean(bool value) => Writes.Add(value);
        public void WriteString(string value) => Writes.Add(value ?? "");
        public void WriteDouble(double value) => Writes.Add(value);
    }
}
