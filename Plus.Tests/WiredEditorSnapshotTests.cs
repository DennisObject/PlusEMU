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
