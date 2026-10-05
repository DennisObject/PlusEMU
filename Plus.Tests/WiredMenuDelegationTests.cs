using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

/// <summary>Fake services prove each owned handler decodes a full frame and makes exactly one service call; the room has no wired component, so any handler lookup would fail.</summary>
public sealed class WiredMenuDelegationTests
{
    [Fact]
    public async Task EachMenuHandlerMakesOneServiceCallOnlyForAFullFrame()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var session = Session();
        var menus = new RecordingMenus();

        await new WiredUserVariablesRequestEvent(menus).Parse(room, session, Packet());
        await new WiredAllVariablesRequestEvent(menus).Parse(room, session, Packet());
        await new WiredVariableHashesEvent(menus).Parse(room, session, Packet(1, "user:10", 123));
        await new WiredVariableHoldersRequestEvent(menus).Parse(room, session, Packet("user:10"));
        await new WiredVariableHoldersPageEvent(menus).Parse(room, session, Packet("user:10", 2, 15, 1, -1));
        await new WiredUserVariableUpdateEvent(menus).Parse(room, session, Packet(1, -2, 12, 50));
        await new WiredUserVariableManageEvent(menus).Parse(room, session, Packet(2, 0, 7, 12, 0));

        Assert.Equal(new[]
        {
            "Snapshot", "CatalogHash", "CatalogDiff user:10=123", "Holders user:10", "HolderPage user:10 2 15 1 -1",
            "Write", "Manage",
        }, menus.Calls);
        Assert.Equal(new WiredVariableMenuWrite(0, WiredVariableTarget.Furni, -2, 12, 50, ""), menus.Writes.Single());
        Assert.Equal(new WiredVariableMenuWrite(2, WiredVariableTarget.User, 7, 12, 0, ""), menus.Manages.Single());
    }

    [Fact]
    public async Task MalformedOrTrailingFramesNeverReachTheMenuService()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var session = Session();
        var menus = new RecordingMenus();

        await new WiredUserVariablesRequestEvent(menus).Parse(room, session, Packet(1));
        await new WiredAllVariablesRequestEvent(menus).Parse(room, session, Packet(1));
        await new WiredVariableHashesEvent(menus).Parse(room, session, Packet(1, "user:10", 1, "user:10", 2));
        await new WiredVariableHoldersRequestEvent(menus).Parse(room, session, Packet(new string('x', 65)));
        await new WiredVariableHoldersPageEvent(menus).Parse(room, session, Packet("user:10", 1, 15, 2, -1));
        await new WiredUserVariableUpdateEvent(menus).Parse(room, session, Packet(0, 5, 0, 12, 0));
        await new WiredUserVariableManageEvent(menus).Parse(room, session, Packet(3, 0, 0, 12, 0));

        Assert.Empty(menus.Calls);
    }

    [Fact]
    public async Task EachMonitorHandlerMakesOneServiceCallOnlyForAFullFrame()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var session = Session();
        var monitor = new RecordingMonitor();

        await new WiredMonitorRequestEvent(monitor).Parse(room, session, Packet());
        await new WiredMonitorRequestEvent(monitor).Parse(room, session, Packet(1));
        await new WiredRoomLogsPageEvent(monitor).Parse(room, session, Packet(3, 50, -1, 8, " GATE "));

        Assert.Equal(new[] { "Monitor 0", "Monitor 1", "Logs 3 50 -1 8  GATE " }, monitor.Calls); // the handler passes the text raw; trimming is the service's job
    }

    [Fact]
    public async Task MalformedOrTrailingMonitorFramesNeverReachTheMonitorService()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var session = Session();
        var monitor = new RecordingMonitor();

        await new WiredMonitorRequestEvent(monitor).Parse(room, session, Packet(2));
        await new WiredMonitorRequestEvent(monitor).Parse(room, session, Packet(0, 0));
        await new WiredRoomLogsPageEvent(monitor).Parse(room, session, Packet(1, 50, 4, -1, ""));
        await new WiredRoomLogsPageEvent(monitor).Parse(room, session, Packet(1, 50, -1, 9, ""));
        await new WiredRoomLogsPageEvent(monitor).Parse(room, session, Packet(1, 50, -1, -1, new string('x', 401)));
        await new WiredRoomLogsPageEvent(monitor).Parse(room, session, Packet(1, 50, -1, -1, "x", 1));

        Assert.Empty(monitor.Calls);
    }

    private static FlashGameClient Session()
    {
        var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient);
        client.SetHabbo(new Habbo { Id = 1, Username = "Alice" });
        return client;
    }

    private static FlashIncomingPacket Packet(params object[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            if (value is int number)
            {
                var bytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(bytes, number);
                stream.Write(bytes);
            }
            else
            {
                var bytes = Encoding.UTF8.GetBytes((string)value);
                var length = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)bytes.Length));
                stream.Write(length);
                stream.Write(bytes);
            }
        }
        return new() { Buffer = stream.ToArray() };
    }

    private sealed class RecordingMenus : IWiredVariableMenuService
    {
        public List<string> Calls { get; } = [];
        public List<WiredVariableMenuWrite> Writes { get; } = [];
        public List<WiredVariableMenuWrite> Manages { get; } = [];
        public void ShowCatalogHash(Room room, GameClient session) => Calls.Add("CatalogHash");
        public void ShowCatalogDiff(Room room, GameClient session, IReadOnlyDictionary<string, int> known) =>
            Calls.Add("CatalogDiff " + string.Join(',', known.Select(pair => $"{pair.Key}={pair.Value}")));
        public void ShowSnapshot(Room room, GameClient session) => Calls.Add("Snapshot");
        public void ShowHolders(Room room, GameClient session, string id) => Calls.Add("Holders " + id);
        public void ShowHolderPage(Room room, GameClient session, string id, int page, int size, int users, int sort) =>
            Calls.Add($"HolderPage {id} {page} {size} {users} {sort}");
        public void Write(Room room, GameClient session, WiredVariableMenuWrite request) { Calls.Add("Write"); Writes.Add(request); }
        public void Manage(Room room, GameClient session, WiredVariableMenuWrite request) { Calls.Add("Manage"); Manages.Add(request); }
    }

    private sealed class RecordingMonitor : IWiredMonitorService
    {
        public List<string> Calls { get; } = [];
        public void ShowMonitor(Room room, GameClient session, int action) => Calls.Add($"Monitor {action}");
        public void ShowLogs(Room room, GameClient session, int page, int size, int level, int source, string query) =>
            Calls.Add($"Logs {page} {size} {level} {source} {query}");
    }
}
