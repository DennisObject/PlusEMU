using System.Buffers.Binary;
using System.Text;
using System.Reflection;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using System.Text.Json;
using Dapper;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableRequestLocalTests
{
    [WiredChestDatabaseFact]
    public Task SuccessfulUpdatePreservesSubsequentNativeReads() => ExtensionWritesNeverChangeSubsequentNativeReadBodies(false, false);
    [WiredChestDatabaseFact]
    public Task RefusedUpdatePreservesSubsequentNativeReads() => ExtensionWritesNeverChangeSubsequentNativeReadBodies(false, true);
    [WiredChestDatabaseFact]
    public Task SuccessfulManagePreservesSubsequentNativeReads() => ExtensionWritesNeverChangeSubsequentNativeReadBodies(true, false);
    [WiredChestDatabaseFact]
    public Task RefusedManagePreservesSubsequentNativeReads() => ExtensionWritesNeverChangeSubsequentNativeReadBodies(true, true);

    private static async Task ExtensionWritesNeverChangeSubsequentNativeReadBodies(bool manage, bool refused)
    {
        using var fixture = new Scope();
        var world = fixture.World;
        var id = refused ? 999 : 10;

        if (manage) {
            await new WiredUserVariableManage64Event(fixture.Service).Parse(world.Room, world.Client, Packet(1, 0, 3, 0, id, 0, 7));
        }
        else {
            await new WiredUserVariableUpdate64Event(fixture.Service).Parse(world.Room, world.Client, Packet(1, 3, 0, id, 0, 7));
        }

        world.Packets.Clear();
        await new WiredUserVariablesRequestEvent(fixture.Service).Parse(world.Room, world.Client, Packet());
        await new WiredVariableHoldersRequestEvent(fixture.Service).Parse(world.Room, world.Client, Packet("room:10"));
        await new WiredVariableHoldersPageEvent(fixture.Service).Parse(world.Room, world.Client, Packet("room:10", 1, 50, 0, -1));
        Assert.Equal(new uint[] { 5103, 9462, 9461 }, world.Packets.Select(packet => packet.Header));
    }

    [WiredChestDatabaseFact]
    public async Task EveryExplicitReadLeavesAllThreeNativeReadsNative()
    {
        using var scope = new Scope();
        await new WiredUserVariablesRequest64Event(scope.Service).Parse(scope.World.Room, scope.World.Client, Packet(1));
        await new WiredVariableHoldersRequest64Event(scope.Service).Parse(scope.World.Room, scope.World.Client, Packet(1, "room:10"));
        await new WiredVariableHoldersPage64Event(scope.Service).Parse(scope.World.Room, scope.World.Client, Packet(1, "room:10", 1, 50, 0, -1));
        Assert.Equal(new uint[] { 9480, 9481, 9482 }, scope.World.Packets.Select(x => x.Header));
        scope.World.Packets.Clear();
        await NativeReads(scope);
        Assert.Equal(new uint[] { 5103, 9462, 9461 }, scope.World.Packets.Select(x => x.Header));
    }

    [WiredChestDatabaseFact]
    public async Task SuccessfulAndRefusedNativeAndExactWritesAndClearsRefreshOnlyTheirOrigin()
    {
        using var scope = new Scope();

        foreach (var exact in new[] { true, false }) {
            foreach (var manage in new[] { true, false }) {
                foreach (var refused in new[] { true, false }) {
                    scope.World.Packets.Clear();
                    var fields = new List<object>();

                    if (exact) {
                        fields.Add(1);
                    }

                    if (manage) {
                        fields.Add(0);
                    }

                    fields.AddRange(new object[] { 3, 0, refused ? 999 : 10 });

                    if (exact) {
                        fields.Add(0);
                    }

                    fields.Add(7);

                    if (manage) {
                        if (exact) {
                            await new WiredUserVariableManage64Event(scope.Service).Parse(scope.World.Room, scope.World.Client, Packet(fields.ToArray()));
                        }
                        else {
                            await new WiredUserVariableManageEvent(scope.Service).Parse(scope.World.Room, scope.World.Client, Packet(fields.ToArray()));
                        }
                    }
                    else {
                        if (exact) {
                            await new WiredUserVariableUpdate64Event(scope.Service).Parse(scope.World.Room, scope.World.Client, Packet(fields.ToArray()));
                        }
                        else {
                            await new WiredUserVariableUpdateEvent(scope.Service).Parse(scope.World.Room, scope.World.Client, Packet(fields.ToArray()));
                        }
                    }

                    Assert.Equal(exact ? 9480u : 5103u, scope.World.Packets.Last().Header);
                    Assert.Equal(refused ? 2 : 1, scope.World.Packets.Count);
                }
            }

            scope.World.Packets.Clear();

            if (exact) {
                await new WiredUserVariableManage64Event(scope.Service).Parse(scope.World.Room, scope.World.Client, Packet(1, 2, 3, 0, 10, 0, 0));
            }
            else {
                await new WiredUserVariableManageEvent(scope.Service).Parse(scope.World.Room, scope.World.Client, Packet(2, 3, 0, 10, 0));
            }

            Assert.Equal(exact ? 9480u : 5103u, scope.World.Packets.Last().Header);
            scope.World.Habbo.Access = EditorTestSupport.Access([]);
            Assert.True(scope.World.Room.GetWired().Settings.TrySave(scope.World.Client, 1, 0, "", out _));
            var (guest, packets) = HabbiconTestSupport.Client(new Plus.HabboHotel.Users.Habbo
            {
                Id = 2,
                Username = "Guest",
                CurrentRoom = scope.World.Room,
                Access = EditorTestSupport.Access([PermissionKeys.RoomRightsAny])
            });

            if (exact) {
                scope.Service.ManageExact(scope.World.Room, guest, new(2, WiredVariableTarget.Global, 0, 10, 0, ""));
            }
            else {
                scope.Service.Manage(scope.World.Room, guest, new(2, WiredVariableTarget.Global, 0, 10, 0, ""));
            }

            Assert.Equal(exact ? 9480u : 5103u, packets.Last().Header);
            Assert.Equal(2, packets.Count);
        }
    }

    [WiredChestDatabaseFact]
    public void FrozenReadRechecksDepartureAndRevokedRightsForEveryFormatAndPayload()
    {
        foreach (var departed in new[] { true, false }) {
            foreach (var exact in new[] { true, false }) {
                foreach (var kind in new[] { 0, 1, 2 }) {
                    using var scope = new Scope(persistent: true);
                    scope.World.Habbo.Access = EditorTestSupport.Access([]);
                    var reads = 0;
                    scope.InstallReadBoundary(() =>
                    {
                        reads++;

                        if (departed) {
                            scope.World.Habbo.CurrentRoom = null!;
                        }
                        else {
                            scope.World.Habbo.Username = "Guest";
                        }
                    });
                    Read(scope, kind, exact);
                    Assert.True(reads > 0);
                    Assert.Empty(scope.World.Packets);
                }
            }
        }
    }

    [WiredChestDatabaseFact]
    public void NativeOverflowRefusesWholePayloadAndExactReadsPreserveFrozenWideValue()
    {
        using var scope = new Scope(persistent: true);
        scope.Service.WriteExact(scope.World.Room, scope.World.Client, new(0, WiredVariableTarget.Global, 0, 10, long.MinValue, ""));

        foreach (var kind in new[] { 0, 1, 2 }) {
            scope.World.Packets.Clear();
            Read(scope, kind, false);
            var refusal = Assert.Single(scope.World.Packets);
            Assert.DoesNotContain(refusal.Header, new uint[] { 5103, 9462, 9461, 9480, 9481, 9482 });
            scope.World.Packets.Clear();
            Read(scope, kind, true);
            Assert.Equal(new uint[] { 9480, 9481, 9482 }[kind], Assert.Single(scope.World.Packets).Header);
        }
    }

    [WiredChestDatabaseFact]
    public void NativePreflightAndComposeShareOneFrozenReadDespiteStoreChange()
    {
        using var scope = new Scope(persistent: true);
        scope.Service.WriteExact(scope.World.Room, scope.World.Client, new(0, WiredVariableTarget.Global, 0, 10, 7, ""));
        scope.World.Packets.Clear();
        var reads = 0;
        scope.InstallReadBoundary(() =>
        {
            reads++;
            scope.Database.Connection.Execute("UPDATE wired_variable_values SET value=@value WHERE definition_id=10", new { value = long.MaxValue });
        });
        scope.Service.ShowSnapshot(scope.World.Room, scope.World.Client);
        Assert.Equal(1, reads);
        var sent = Assert.Single(scope.World.Packets);
        Assert.Equal(5103u, sent.Header);
        var payload = new FlashIncomingPacket { Buffer = sent.Payload };
        Assert.Equal(42, payload.ReadInt());

        for (var i = 0; i < 4; i++) {
            Assert.Equal(0, payload.ReadInt());
        }

        Assert.Equal(1, payload.ReadInt());
        Assert.Equal(10, payload.ReadInt());
        Assert.Equal("exact_score", payload.ReadString());
        Assert.True(payload.ReadBool());
        Assert.Equal(10, payload.ReadInt());
        payload.ReadBool();
        payload.ReadBool();
        Assert.Equal(1, payload.ReadInt());
        Assert.Equal(10, payload.ReadInt());
        Assert.True(payload.ReadBool());
        Assert.Equal(7, payload.ReadInt());
        Assert.Equal(long.MaxValue, scope.Database.Connection.QuerySingle<long>("SELECT value FROM wired_variable_values WHERE definition_id=10"));
    }

    private static void Read(Scope scope, int kind, bool exact)
    {
        if (kind == 0) {
            if (exact) {
                scope.Service.ShowExactSnapshot(scope.World.Room, scope.World.Client);
            }
            else {
                scope.Service.ShowSnapshot(scope.World.Room, scope.World.Client);
            }
        }

        if (kind == 1) {
            if (exact) {
                scope.Service.ShowExactHolders(scope.World.Room, scope.World.Client, "room:10");
            }
            else {
                scope.Service.ShowHolders(scope.World.Room, scope.World.Client, "room:10");
            }
        }

        if (kind == 2) {
            if (exact) {
                scope.Service.ShowExactHolderPage(scope.World.Room, scope.World.Client, "room:10", 1, 50, 0, -1);
            }
            else {
                scope.Service.ShowHolderPage(scope.World.Room, scope.World.Client, "room:10", 1, 50, 0, -1);
            }
        }
    }
    private static async Task NativeReads(Scope scope)
    {
        await new WiredUserVariablesRequestEvent(scope.Service).Parse(scope.World.Room, scope.World.Client, Packet());
        await new WiredVariableHoldersRequestEvent(scope.Service).Parse(scope.World.Room, scope.World.Client, Packet("room:10"));
        await new WiredVariableHoldersPageEvent(scope.Service).Parse(scope.World.Room, scope.World.Client, Packet("room:10", 1, 50, 0, -1));
    }

    internal sealed class Scope : IDisposable
    {
        internal WiredChestDatabaseTests.Fixture Database { get; } = new();
        internal WiredChestProtocolTests.World World { get; }
        internal WiredVariableMenuService Service { get; } = new();
        internal Scope(bool persistent = false)
        {
            if (persistent) {
                Database.Connection.Execute("ALTER TABLE users ADD COLUMN username VARCHAR(32) NOT NULL DEFAULT ''; ALTER TABLE furniture ADD COLUMN public_name VARCHAR(100) NOT NULL DEFAULT '';");
                Database.Connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/15_AddWiredVariableValues.sql")));
                Database.Connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/39_UseUtcWiredVariableTimes.sql")));
                Database.Connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/66_WiredVariableValueWidth.sql")));
            }

            var config = new WiredConfiguration { Text = "exact_score", IntParams = [persistent ? 10 : 1, 0] };
            Database.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(10,1,42,1,''); INSERT INTO wired_item_configurations VALUES(10,'wf_var_room',@config)", new { config = JsonSerializer.Serialize(config) });
            World = new(Database.Database);
            World.Room.Type = "private";
            World.Room.OwnerName = "Owner";
            World.Habbo.Username = "Owner";
            var variables = World.Room.GetWired().Variables;
            var box = Assert.IsType<WiredVariableDefinitionBox>(variables.CreateBox(new Item
            {
                Id = 10,
                OwnerId = 1,
                RoomId = 42,
                Definition = new() { ItemName = "wf_var_room" }
            }));
            box.ApplyConfiguration(config);
            variables.ConfigurationLoaded(box);
        }
        internal void InstallReadBoundary(Action onRead)
        {
            var store = new BoundaryStore(new DatabaseWiredVariableStore(Database.Database), onRead);
            var module = new WiredVariableModule(42, new DatabaseWiredVariableDirectory(Database.Database), store, TimeProvider.System);
            typeof(WiredRoomVariables).GetField("<Module>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(World.Room.GetWired().Variables, module);
        }
        public void Dispose() => Database.Dispose();
    }

    private sealed class BoundaryStore(IWiredVariableStore inner, Action onRead) : IWiredVariableStore
    {
        public WiredVariableValue? Read(WiredVariableKey key)
        {
            var value = inner.Read(key);
            onRead();

            return value;
        }
        public IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> GetHolders(uint definitionId)
        {
            var value = inner.GetHolders(definitionId);
            onRead();

            return value;
        }
        public IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> ReadMany(IReadOnlyCollection<WiredVariableKey> keys)
        {
            var value = inner.ReadMany(keys);
            onRead();

            return value;
        }
        public WiredVariableHolderPage ReadPage(uint id, WiredVariableTarget target, int page, int size, int sort, IReadOnlyCollection<long>? filter = null, IReadOnlyDictionary<long, string>? names = null)
        {
            var value = inner.ReadPage(id, target, page, size, sort, filter, names);
            onRead();

            return value;
        }
        public WiredVariableWrite Mutate(WiredVariableKey key, Func<WiredVariableValue?, WiredVariableValue?> update, WiredVariableAuthorization? authorization = null) => inner.Mutate(key, update, authorization);
        public int DeleteDefinition(uint id) => inner.DeleteDefinition(id);
    }

    internal static FlashIncomingPacket Packet(params object[] fields)
    {
        using var stream = new MemoryStream();

        foreach (var field in fields) {
            if (field is int value) {
                var bytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(bytes, value);
                stream.Write(bytes);
            }
            else {
                var bytes = Encoding.UTF8.GetBytes((string)field);
                var length = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)bytes.Length));
                stream.Write(length);
                stream.Write(bytes);
            }
        }

        return new() { Buffer = stream.ToArray() };
    }
}
