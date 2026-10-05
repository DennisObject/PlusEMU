using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms.Instance;
using Xunit;

namespace Plus.Tests;

public class WiredPacketFreezeTests
{
    [Fact]
    public void CatalogMenuAndHolderPacketsFreezeListsAndNestedTextConnectors()
    {
        var connector = new Dictionary<int, string> { [1] = "one" };
        var variable = new WiredVariableDescription(new(10, 1, 5, "score", WiredVariableTarget.User,
            WiredVariableAvailability.Persistent, true), true, false) { TextConnector = connector };
        var definitions = new List<WiredVariableDescription> { variable };
        var removed = new List<string> { "user:9" };
        var holders = new List<WiredVariableStoredHolder> { new(new(10, WiredVariableTarget.User, 7), "Alice", new(25, 1000, 2000)) };
        var composers = new IServerPacket[]
        {
            new WiredAllVariablesDiffComposer(new(42, true, removed, definitions)),
            new WiredVariableHoldersComposer(1, variable, holders),
            new WiredVariableHoldersPageComposer("user:10", new(1, 1, 10, holders), 0, 0),
            new WiredUserVariablesDataComposer(new(1, definitions, holders))
        };
        var expected = composers.Select(Write).ToArray();
        Assert.Equal(new object[] { 42, true, 1, "user:9", 1, variable.Hash, "user:10", 1, "score", 10, 1,
            true, true, true, true, false, false, true, true, true, 1, 1, "one" }, expected[0]);
        Assert.Contains("Alice", expected[2]);
        Assert.Contains("[{\"itemId\":10,\"variableType\":2,\"textConnector\":[{\"key\":1,\"value\":\"one\"}]}]", expected[3]);

        connector[1] = "changed";
        connector[2] = "two";
        removed.Clear();
        definitions.Clear();
        holders.Clear();
        for (var index = 0; index < composers.Length; index++)
        {
            Assert.Equal(expected[index], Write(composers[index]));
            Assert.Equal(expected[index], Write(composers[index]));
        }
    }

    [Fact]
    public void MonitorAndLogPageFreezeCallerCollections()
    {
        var entry = new WiredRoomLogEntry(1, 2, WiredLogSource.WiredLog, 7, "label", "message", new(2040, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var entries = new List<WiredRoomLogEntry> { entry };
        var tallies = new List<WiredRoomLogTally> { new(WiredLogSource.WiredLog, 1, entry) };
        var monitor = new WiredMonitorDataComposer(new(new(0, 0, 0, 0, 0, 0), 100, 100, 10, new(tallies, entries)));
        var page = new WiredRoomLogPageComposer(new(1, 0, 10, entries), -1, -1, "");
        var monitorWrites = Write(monitor);
        var pageWrites = Write(page);
        Assert.Contains("message", monitorWrites);
        Assert.Contains("message", pageWrites);

        entries.Clear();
        tallies.Clear();
        Assert.Equal(monitorWrites, Write(monitor));
        Assert.Equal(monitorWrites, Write(monitor));
        Assert.Equal(pageWrites, Write(page));
        Assert.Equal(pageWrites, Write(page));
    }

    private static object[] Write(IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);
        return packet.Writes.ToArray();
    }
}
