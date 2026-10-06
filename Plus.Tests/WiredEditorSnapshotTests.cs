using System.Collections.Concurrent;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredEditorSnapshotTests
{
    [Theory]
    [InlineData(WiredBoxType.EffectMatchPosition, "1;0;1", "1;0;1", 3, 1, 0, 1, 3)]
    [InlineData(WiredBoxType.EffectMoveAndRotate, "2;1", "2;1", 2, 2, 1, 0, 4)]
    [InlineData(WiredBoxType.EffectMuteTriggerer, "5;quiet", "5;quiet", 1, 5, 0, 0, 20)]
    [InlineData(WiredBoxType.EffectBotFollowsUserBox, "1;Bot", "Bot", 1, 1, 0, 0, 25)]
    [InlineData(WiredBoxType.EffectBotGivesHanditemBox, "Bot;12", "Bot", 1, 12, 0, 0, 24)]
    public void EffectShapesCaptureExactFieldsAndFreezeItemsDelayAndBlockedList(
        WiredBoxType type, string text, string wireText, int count, int first, int second, int third, int code)
    {
        var box = Box(type, text);
        var blocked = new List<int> { 33 };
        var composer = new WiredEffectConfigComposer(WiredEditorSnapshot.Effect(box, blocked));
        Mutate(box, blocked);
        var expected = new List<object> { false, 15, 1, 8u, 91, 7u, wireText, count };
        expected.AddRange(new[] { first, second, third }.Take(count).Select(value => (object)value));
        expected.AddRange([0, code, 9, 1, 33]);
        Recompose(composer, expected);
    }

    [Fact]
    public void EmptyConditionDefaultsAreCapturedWithoutMutatingTheLiveBox()
    {
        var box = Box(WiredBoxType.ConditionMatchStateAndPosition, "");
        var composer = new WiredConditionConfigComposer(WiredEditorSnapshot.Condition(box));
        Assert.Equal("", box.StringData);
        Mutate(box, []);
        Recompose(composer, [false, 5, 1, 8u, 91, 7u, "", 3, 0, 0, 0, 0, 0]);
    }

    [Theory]
    [InlineData(WiredBoxType.ConditionUserCountInRoom, "2;50", 5, 2, 2, 50)]
    [InlineData(WiredBoxType.ConditionUserCountDoesntInRoom, "2;50", 16, 2, 2, 50)]
    [InlineData(WiredBoxType.ConditionFurniHasNoFurni, "1", 18, 1, 1, 0)]
    public void ConditionShapesPreserveExactParameterCounts(WiredBoxType type, string text, int code, int count, int first, int second)
    {
        var composer = new WiredConditionConfigComposer(WiredEditorSnapshot.Condition(Box(type, text)));
        var expected = new List<object> { false, 5, 1, 8u, 91, 7u, text, count, first };

        if (count == 2) {
            expected.Add(second);
        }

        expected.AddRange([0, code]);
        Recompose(composer, expected);
    }

    [Fact]
    public void TriggerAndConfiguredViewsCopyAllConstructorData()
    {
        var box = Box(WiredBoxType.TriggerRepeat, "trigger");
        var blocked = new List<int> { 33 };
        var trigger = new WiredTriggeRconfigComposer(WiredEditorSnapshot.Trigger(box, blocked));
        var configured = new WiredConfiguredConfigComposer(WiredEditorSnapshot.Capture(box.Item,
            new("test", WiredBoxCategory.Action, 123, 0, "test"),
            new() { Text = "configured", IntParams = [4, 5], SelectedItems = [8], Delay = 3, SelectionCode = 2 }, 20, blocked));
        Mutate(box, blocked);
        Recompose(trigger, [false, 5, 1, 8u, 91, 7u, "trigger", 1, 9, 0, 6, 1, 33]);
        Recompose(configured, [false, 20, 1, 8u, 91, 7u, "configured", 2, 4, 5, 2, 123, 3, 1, 33]);
    }

    private static CycleBox Box(WiredBoxType type, string text) => new(type)
    {
        Item = new() { Id = 7, Definition = new() { SpriteId = 91 } },
        StringData = text,
        Delay = 9,
        SetItems = new(new[] { new KeyValuePair<uint, Item>(8, new() { Id = 8 }) })
    };

    private static void Mutate(CycleBox box, List<int> blocked)
    {
        box.Item.Id = 99;
        box.Item.Definition.SpriteId = 100;
        box.SetItems[8].Id = 88;
        box.SetItems.Clear();
        box.StringData = "changed";
        box.Delay = 50;
        blocked.Clear();
    }

    private static void Recompose(IServerPacket composer, IEnumerable<object> expected)
    {
        for (var index = 0; index < 2; index++) {
            var output = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(output);
            Assert.Equal(expected, output.Writes);
        }
    }

    private sealed class CycleBox(WiredBoxType type) : IWiredItem, IWiredCycle
    {
        public Room Instance { get; set; } = null!;
        public Item Item { get; set; } = null!;
        public WiredBoxType Type => type;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public int Delay { get; set; }
        public int TickCount { get; set; }
        public bool OnCycle() => throw new NotSupportedException();
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] parameters) => throw new NotSupportedException();
    }
}
