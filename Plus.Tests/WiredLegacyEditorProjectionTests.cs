using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Boxes.Conditions;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Items.Wired.Boxes.Triggers;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms.Instance;
using Xunit;

namespace Plus.Tests;

public class WiredLegacyEditorProjectionTests
{
    [Theory]
    [InlineData(0, 7)]
    [InlineData(1, 0)]
    [InlineData(2, 8)]
    [InlineData(3, 5)]
    public void ExistingBoxesWithCustomFurnitureNamesOpenWithCurrentEditorFields(int shape, int editorCode)
    {
        var original = Create(shape);
        original.BoolData = true;
        original.ItemsData = "saved legacy bytes";
        var storedText = original.StringData;
        var picked = original.SetItems;
        Assert.True(WiredLegacyEditorProjection.TryGetConfiguration(original, out var descriptor, out var configuration));
        var parameters = shape switch
        {
            0 => new[] { 0, 0, 34, -1 },
            1 => new[] { 0, 1, 1 },
            2 => new[] { 0, 100, 0 },
            _ => new[] { 2, 8, 0 }
        };
        Assert.Equal(parameters, configuration.IntParams);
        Assert.Equal(editorCode, descriptor.EditorCode);
        var packet = new RecordingPacket();
        var composer = new WiredConfiguredConfigComposer(original.Item, descriptor, configuration);
        composer.Compose(packet);
        var expected = new List<object> { false, 100, configuration.SelectedItems.Length };
        expected.AddRange(configuration.SelectedItems.Select(id => (object)id));
        expected.AddRange(new object[] { 91, 7u, shape is 0 or 1 ? storedText : "", parameters.Length });
        expected.AddRange(parameters.Select(value => (object)value));
        expected.AddRange(new object[] { 0, editorCode });
        if (shape is 0 or 2) expected.Add(configuration.Delay);
        if (shape != 3) expected.Add(0);
        Assert.Equal(expected, packet.Writes);
        Assert.Equal(storedText, original.StringData);
        Assert.True(original.BoolData);
        Assert.Equal("saved legacy bytes", original.ItemsData);
        Assert.Same(picked, original.SetItems);
    }

    [Fact]
    public void SnapshotReopenUsesStoredCoordinatesAndStateRatherThanCurrentFurniture()
    {
        var original = new MatchPositionBox(null!, Item())
        {
            StringData = "1;0;1", ItemsData = "8:1,2,3.5,4,old-state;", Delay = 9
        };
        original.SetItems.TryAdd(8, new Item { Id = 8, Definition = new() { Id = 71 }, LegacyDataString = "current-state" });
        Assert.True(WiredLegacyEditorProjection.TryGetConfiguration(original, out _, out var configuration));
        Assert.Equal(new WiredFurniSnapshot(8, 71, 1, 2, 3.5, 4, "old-state"), Assert.Single(configuration.Snapshots));
        Assert.Equal(9, configuration.Delay);
        Assert.Equal("8:1,2,3.5,4,old-state;", original.ItemsData);
        original.ItemsData = "8:invalid;";
        Assert.False(WiredLegacyEditorProjection.TryGetConfiguration(original, out _, out _));
        Assert.Equal("8:invalid;", original.ItemsData);
    }

    [Fact]
    public void EveryConstructibleLegacyBoxHasAnExplicitCanonicalOrCustomEditorDescriptor()
    {
        var wired = new WiredComponent(null!);
        var count = 0;
        foreach (var type in Enum.GetValues<WiredBoxType>().Where(WiredBoxTypeUtility.IsLegacyConstructible))
        {
            var item = Item();
            item.Definition.WiredType = type;
            var box = Assert.IsAssignableFrom<IWiredItem>(wired.GenerateNewBox(item));
            if (WiredLegacyCustomEditor.IsCustom(box))
                Assert.True(WiredLegacyCustomEditor.TryGetConfiguration(box, out _, out _));
            else
            {
                Assert.True(WiredLegacyEditorProjection.TryGetDescriptor(box, out var descriptor), type.ToString());
                Assert.Same(descriptor, WiredBoxRegistry.All.Single(entry => entry.CanonicalName == descriptor.CanonicalName));
                Assert.Equal(WiredBoxTypeUtility.GetWiredId(type), descriptor.EditorCode);
            }
            count++;
        }
        Assert.Equal(50, count);
    }

    private static Item Item() => new() { Id = 7, Definition = new() { SpriteId = 91, ItemName = "deployment_custom_name" } };

    private static IWiredItem Create(int shape)
    {
        var item = Item();
        IWiredItem box = shape switch
        {
            0 => new ShowMessageBox(null!, item) { StringData = "Hello %USERNAME%" },
            1 => new UserSaysBox(null!, item) { StringData = "hello" },
            2 => new TeleportUserBox(null!, item) { Delay = 6 },
            _ => new UserCountInRoomBox(null!, item) { StringData = "2;8" }
        };
        if (shape == 2) box.SetItems.TryAdd(8, new() { Id = 8, Definition = new() });
        return box;
    }

    private sealed class RecordingPacket : IOutgoingPacket
    {
        public List<object> Writes { get; } = [];
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
        public void WriteString(string value) => Writes.Add(value);
        public void WriteDouble(double value) => Writes.Add(value);
    }
}
