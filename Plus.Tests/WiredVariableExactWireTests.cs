using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.WiredVariables;
using Dapper;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;
using static Plus.Tests.HabbiconTestSupport;

namespace Plus.Tests;

public sealed class WiredVariableExactWireTests
{
    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(9007199254740993L)]
    [InlineData(-9007199254740993L)]
    [InlineData(-2147483649L)]
    [InlineData(2147483648L)]
    public void ExactSnapshotHolderPageAndWriteRetainEverySignedBit(long value)
    {
        var hi = unchecked((int)(value >> 32));
        var lo = unchecked((int)value);
        Assert.True(WiredUserVariableUpdateEvent.TryRead(Packet(1, 3, 0, 10, hi, lo), false, out var write, exact: true));
        Assert.Equal(value, write!.Value);
        Assert.True(WiredUserVariableUpdateEvent.TryRead(Packet(1, 0, 3, 0, 10, hi, lo), true, out var manage, exact: true));
        Assert.Equal(value, manage!.Value);
        var variable = new WiredVariableDescription(new(10, 42, 1, "exact", WiredVariableTarget.Global,
            WiredVariableAvailability.Persistent, true), true, false);
        var holder = new WiredVariableStoredHolder(new(10, WiredVariableTarget.Global, 0), "", new(value, null, null));
        var snapshot = new WiredUserVariablesDataComposer(new(42, [variable], [holder]), exact: true);
        var packet = new RecordingPacket();
        snapshot.Compose(packet);
        Assert.Equal(9480u, snapshot.MessageId);
        Assert.Equal(new object[] { 1, 42u, 0, 0, 0, 0, 1, 10u, "exact", true, 10, false, false,
            1, 10u, true, hi, lo, 0, 0, 0 }, packet.Writes);
        var holders = new RecordingPacket();
        var composer = new WiredVariableHoldersComposer(42, variable, [holder], exact: true);
        composer.Compose(holders);
        Assert.Equal(9481u, composer.MessageId);
        Assert.Equal(new object[] { 1, 0, hi, lo }, holders.Writes.TakeLast(4));
        var page = new RecordingPacket();
        var pageComposer = new WiredVariableHoldersPageComposer("room:10", new(1, 1, 50, [holder]), 0, -1, exact: true);
        pageComposer.Compose(page);
        Assert.Equal(9482u, pageComposer.MessageId);
        Assert.Equal(new object[] { 1, "room:10", 1, 1, 50, 1, 0, 0, "", hi, lo, 0, 0, "", 0, 0, "", 0, -1 }, page.Writes);
    }

    [Fact]
    public async Task CapabilityChangesOnlyAfterCompleteValidFrameAndStaysWithConnectionThroughRefresh()
    {
        var (client, _) = Client(new Habbo { Id = 1 });
        var (second, _) = Client(new Habbo { Id = 1 });
        var menus = new Menus();
        var request = new WiredUserVariablesRequestEvent(menus);

        foreach (var bad in new[] { Packet(2), Packet(1, 0), new FlashIncomingPacket { Buffer = new byte[] { 0, 0, 0 } } }) {
            await request.Parse(null!, client, bad);
            Assert.False(WiredVariableWireProtocol.IsExact(client));
        }

        await new WiredUserVariableUpdate64Event(menus).Parse(null!, client, Packet(2, 3, 0, 10, 1, 2));
        Assert.False(WiredVariableWireProtocol.IsExact(client));
        await new WiredUserVariableUpdate64Event(menus).Parse(null!, client, Packet(1, 3, 0, 10, 1, 2));
        Assert.True(WiredVariableWireProtocol.IsExact(client));
        Assert.Equal(4294967298L, Assert.Single(menus.Writes).Value);
        await request.Parse(null!, client, Packet());
        await new WiredUserVariableManage64Event(menus).Parse(null!, client, Packet(1, 1, 3, 0, 10, 0, 0));
        await request.Parse(null!, second, Packet());
        Assert.Equal(new[] { true, true, true, false }, menus.Snapshots);
        Assert.False(WiredVariableWireProtocol.IsExact(second));
    }

    [WiredChestDatabaseFact]
    public async Task ActualMenuServiceKeepsExactRefreshReportsFailedWriteAndPreflightsLegacyResponses()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        var config = new WiredConfiguration { Text = "exact_score", IntParams = [1, 0] };
        fixture.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(10,1,42,1,''); INSERT INTO wired_item_configurations VALUES(10,'wf_var_room',@config)",
            new { config = JsonSerializer.Serialize(config) });
        var world = new WiredChestProtocolTests.World(fixture.Database);
        world.Room.Type = "private";
        world.Room.OwnerName = "Owner";
        world.Habbo.Username = "Owner";
        var variables = world.Room.GetWired().Variables;
        var box = Assert.IsType<WiredVariableDefinitionBox>(variables.CreateBox(new Item
        {
            Id = 10,
            OwnerId = 1,
            RoomId = 42,
            Definition = new() { ItemName = "wf_var_room" }
        }));
        box.ApplyConfiguration(config);
        variables.ConfigurationLoaded(box);
        var service = new WiredVariableMenuService();
        await new WiredUserVariableUpdate64Event(service).Parse(world.Room, world.Client,
            Packet(1, 3, 0, 10, int.MaxValue, -1));
        Assert.True(WiredVariableWireProtocol.IsExact(world.Client));
        Assert.Equal(long.MaxValue, Assert.Single(new WiredVariableMenu(world.Room, variables).Snapshot().Assignments).Value.Value);
        Assert.Equal(9480u, Assert.Single(world.Packets).Header);
        world.Packets.Clear();
        await new WiredUserVariablesRequestEvent(service).Parse(world.Room, world.Client, Packet());
        service.Manage(world.Room, world.Client, new(0, WiredVariableTarget.Global, 0, 10, long.MinValue, ""));
        Assert.Equal(new uint[] { 9480, 9480 }, world.Packets.Select(x => x.Header));
        Assert.Equal(long.MinValue, Assert.Single(new WiredVariableMenu(world.Room, variables).Snapshot().Assignments).Value.Value);
        world.Packets.Clear();
        service.Write(world.Room, world.Client, new(0, WiredVariableTarget.Global, 0, 999, 123, ""));
        Assert.Equal(2, world.Packets.Count);
        Assert.NotEqual(9480u, world.Packets[0].Header); // Visible failure precedes the authoritative refresh.
        Assert.Equal(9480u, world.Packets[1].Header);
        Assert.Equal(long.MinValue, Assert.Single(new WiredVariableMenu(world.Room, variables).Snapshot().Assignments).Value.Value);
        var (legacy, packets) = Client(new Habbo { Id = 1, Username = "Owner", CurrentRoom = world.Room });
        service.ShowSnapshot(world.Room, legacy);
        service.ShowHolders(world.Room, legacy, "room:10");
        service.ShowHolderPage(world.Room, legacy, "room:10", 1, 50, 0, -1);
        Assert.Equal(3, packets.Count);
        Assert.DoesNotContain(packets, packet => packet.Header is 5103 or 9461 or 9462 or 9480 or 9481 or 9482);
        Assert.False(WiredVariableWireProtocol.IsExact(legacy));
        world.Habbo.Access = EditorTestSupport.Access([]);
        Assert.True(world.Room.GetWired().Settings.TrySave(world.Client, 1, 0, "", out _));
        var (guest, guestPackets) = Client(new Habbo { Id = 2, Username = "Guest", CurrentRoom = world.Room, Access = EditorTestSupport.Access([]) });
        WiredVariableWireProtocol.Enable(guest);
        Assert.True(world.Room.GetWired().Settings.CanInspect(guest));
        Assert.False(world.Room.GetWired().Settings.CanModify(guest));
        service.Write(world.Room, guest, new(0, WiredVariableTarget.Global, 0, 10, 123, ""));
        Assert.Equal(2, guestPackets.Count);
        Assert.NotEqual(9480u, guestPackets[0].Header);
        Assert.Equal(9480u, guestPackets[1].Header);
        Assert.Equal(long.MinValue, Assert.Single(new WiredVariableMenu(world.Room, variables).Snapshot().Assignments).Value.Value);
        guest.GetHabbo().Access = EditorTestSupport.Access([PermissionKeys.RoomRightsAny]);
        guestPackets.Clear();
        service.Manage(world.Room, guest, new(2, WiredVariableTarget.Global, 0, 10, 0, ""));
        Assert.Equal(2, guestPackets.Count);
        Assert.NotEqual(9480u, guestPackets[0].Header);
        Assert.Equal(9480u, guestPackets[1].Header);
        Assert.Equal(long.MinValue, Assert.Single(new WiredVariableMenu(world.Room, variables).Snapshot().Assignments).Value.Value);
    }

    [Fact]
    public void LegacyWidePreflightRefusesVisiblyAndLegacyInt32WriteStillHasSameShape()
    {
        var (client, packets) = Client(new Habbo { Id = 1 });
        var holder = new WiredVariableStoredHolder(new(10, WiredVariableTarget.Global, 0), "", new(long.MaxValue, null, null));
        Assert.False(WiredVariableWireProtocol.CanSend(client, [holder]));
        Assert.NotEmpty(packets);
        Assert.DoesNotContain(packets, packet => packet.Header is 5103 or 9461 or 9462);
        Assert.True(WiredUserVariableUpdateEvent.TryRead(Packet(3, 0, 10, int.MinValue), false, out var legacy));
        Assert.Equal(int.MinValue, legacy!.Value);
        WiredVariableWireProtocol.Enable(client);
        Assert.True(WiredVariableWireProtocol.CanSend(client, [holder]));
    }

    [Fact]
    public void ExactWritesRejectVersionsTruncationTrailingDataAndInvalidDomainTargets()
    {
        foreach (var bad in new[] { Packet(0, 3, 0, 10, 0, 0), Packet(2, 3, 0, 10, 0, 0),
            Packet(1, 3, 0, 10, 0), Packet(1, 2, 0, 10, 0, 0), Packet(1, 3, 0, -1, 0, 0),
            Packet(1, 3, 0, 10, 0, 0, "", 1), Packet(1, 3, 0, 10, 0, 0, "custom:10") }) {
            Assert.False(WiredUserVariableUpdateEvent.TryRead(bad, false, out _, exact: true));
        }

        Assert.False(WiredUserVariableUpdateEvent.TryRead(Packet(1, 3, 3, 0, 10, 0, 0), true, out _, exact: true));
        Assert.False(WiredUserVariableUpdateEvent.TryRead(Packet(1, 0, 3, 0, 10, 0, 0, ""), true, out _, exact: true));
        Assert.True(WiredUserVariableUpdateEvent.TryRead(Packet(1, 1, 42, 0, 0, 5, "internal:~background_color.hue"), false, out var smart, exact: true));
        Assert.Equal("internal:~background_color.hue", smart!.Token);
    }

    [Fact]
    public void EveryRevisionRegistersExactDirectionHeadersWithoutChangingLegacyMappings()
    {
        foreach (var filename in new[] { "1.6.6.json", "3.6.0.json", "example.json", "OCTANE-3-6-0-FLOOR-20260909.json" }) {
            using var json = JsonDocument.Parse(File.ReadAllText(HabbiconPacketTests.Repo("Resources/Revisions/" + filename)));
            var incoming = json.RootElement.GetProperty("IncomingHeaders");
            var outgoing = json.RootElement.GetProperty("OutgoingHeaders");
            Assert.Equal(10110, incoming.GetProperty("WiredUserVariableUpdate64Event").GetInt32());
            Assert.Equal(10111, incoming.GetProperty("WiredUserVariableManage64Event").GetInt32());
            Assert.Equal(10025, incoming.GetProperty("WiredUserVariableUpdateEvent").GetInt32());
            Assert.Equal(9480, outgoing.GetProperty("WiredUserVariablesData64Composer").GetInt32());
            Assert.Equal(9481, outgoing.GetProperty("WiredVariableHolders64Composer").GetInt32());
            Assert.Equal(9482, outgoing.GetProperty("WiredVariableHoldersPage64Composer").GetInt32());
            Assert.Equal(5103, outgoing.GetProperty("WiredUserVariablesDataComposer").GetInt32());
        }
    }

    private sealed class Menus : IWiredVariableMenuService
    {
        public List<bool> Snapshots = [];
        public List<WiredVariableMenuWrite> Writes = [];
        public void ShowSnapshot(Room room, GameClient session) => Snapshots.Add(WiredVariableWireProtocol.IsExact(session));
        public void Write(Room room, GameClient session, WiredVariableMenuWrite request)
        {
            Writes.Add(request);
            ShowSnapshot(room, session);
        }
        public void Manage(Room room, GameClient session, WiredVariableMenuWrite request) => ShowSnapshot(room, session);
        public void ShowCatalogHash(Room room, GameClient session) { }
        public void ShowCatalogDiff(Room room, GameClient session, IReadOnlyDictionary<string, int> known) { }
        public void ShowHolders(Room room, GameClient session, string id) { }
        public void ShowHolderPage(Room room, GameClient session, string id, int page, int size, int users, int sort) { }
    }
    private static FlashIncomingPacket Packet(params object[] values)
    {
        using var stream = new MemoryStream();

        foreach (var value in values) {
            if (value is int number) {
                var bytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(bytes, number);
                stream.Write(bytes);
            }
            else {
                var bytes = Encoding.UTF8.GetBytes((string)value);
                var length = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)bytes.Length));
                stream.Write(length);
                stream.Write(bytes);
            }
        }

        return new() { Buffer = stream.ToArray() };
    }
}
