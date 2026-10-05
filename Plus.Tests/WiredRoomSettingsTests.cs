using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.Communication.Revisions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class WiredRoomSettingsTests
{
    [Theory]
    [InlineData(16, 2, "UTC")]
    [InlineData(2, 1, "UTC")]
    [InlineData(-1, 2, "UTC")]
    [InlineData(2, -2, "UTC")]
    [InlineData(2, 2, "invalid-zone-for-wired")]
    public void InvalidMasksOrTimezoneAreRejected(int inspect, int modify, string timezone) =>
        Assert.False(WiredRoomSettingsSnapshot.TryValidate(inspect, modify, timezone, out _));

    [Fact]
    public void NoSavedRowPreservesExistingPlusDecoratorAndGroupAdminRights()
    {
        var room = Room(); var rights = Client(room, 2); room.UsersWithRights.Add(2);
        var admin = Client(room, 3); var guest = Client(room, 4); Set(room.Group, "_administrators", new List<int> { 3 });
        var settings = new WiredRoomSettings(room, new MemoryStore());
        Assert.True(settings.CanModify(rights)); Assert.True(settings.CanInspect(rights));
        Assert.True(settings.CanModify(admin)); Assert.True(settings.CanInspect(admin));
        Assert.False(settings.CanModify(guest)); Assert.False(settings.CanInspect(guest));
    }

    [Theory]
    [InlineData(0, false, false, false)]
    [InlineData(1, true, true, true)]
    [InlineData(2, true, false, false)]
    [InlineData(4, false, true, true)]
    [InlineData(8, false, false, true)]
    public void ExplicitInspectMasksDistinguishRightsMembersAndAdmins(int mask, bool rightsAllowed, bool memberAllowed, bool adminAllowed)
    {
        var room = Room(); room.UsersWithRights.Add(2); Set(room.Group, "_members", new List<int> { 3 });
        Set(room.Group, "_administrators", new List<int> { 4 });
        var settings = new WiredRoomSettings(room, new MemoryStore { Saved = new(mask, 0) });
        Assert.Equal(rightsAllowed, settings.CanInspect(Client(room, 2)));
        Assert.Equal(memberAllowed, settings.CanInspect(Client(room, 3)));
        Assert.Equal(adminAllowed, settings.CanInspect(Client(room, 4)));
        Assert.False(settings.CanModify(Client(room, 2)));
        Assert.True(settings.CanManage(Client(room, 1))); Assert.True(settings.CanModify(Client(room, 1)));
        Assert.True(settings.CanManage(Client(room, 5, "room.owner.any")));
        Assert.True(settings.CanInspect(Client(room, 6, "room.rights.any")));
    }

    [Fact]
    public void SavePersistsBeforePublicationAndFailurePreservesLiveSnapshot()
    {
        var room = Room(); var owner = Client(room, 1); var store = new MemoryStore { Saved = new(2, 2, "UTC") };
        var settings = new WiredRoomSettings(room, store, () => TimeZoneInfo.Utc);
        var original = settings.Snapshot;
        store.BeforeSave = () => Assert.Same(original, settings.Snapshot);
        Assert.True(settings.TrySave(owner, 15, 14, "Europe/Berlin", out _));
        Assert.Equal(store.Saved, settings.Snapshot); Assert.Equal("Europe/Berlin", settings.TimeZone.Id);
        var accepted = settings.Snapshot; store.Fail = true;
        store.BeforeSave = () => Assert.Same(accepted, settings.Snapshot);
        Assert.Throws<InvalidOperationException>(() => settings.TrySave(owner, 0, 0, "UTC", out _));
        Assert.Same(accepted, settings.Snapshot); Assert.Equal(accepted, store.Saved);
        Assert.False(settings.TrySave(Client(room, 7), 1, 0, "UTC", out _));
        Assert.False(settings.TrySave(owner, 1, 1, "UTC", out _));
        Assert.False(settings.TrySave(owner, 1, 0, new string('z', 65), out _));
        owner.GetHabbo().CurrentRoom = null;
        Assert.False(settings.CanManage(owner)); Assert.False(settings.CanInspect(owner)); Assert.False(settings.CanModify(owner));
    }

    [Fact]
    public async Task ActualSettingsRequestRefreshesStaleExpectedRowBeforeTheNextSave()
    {
        var room = Room(); var owner = Client(room, 1);
        var store = new MemoryStore { Saved = new(2, 2, "UTC") };
        var settings = Register(room, store); var replies = Capture(owner);
        var initial = settings.Snapshot;
        store.Saved = new(1, 0, "Europe/Berlin");
        Assert.Throws<InvalidOperationException>(() => settings.TrySave(owner, 2, 2, "UTC", out _));
        Assert.Same(initial, settings.Snapshot);
        await new WiredRoomSettingsRequestEvent(Service()).Parse(room, owner, new FlashIncomingPacket { Buffer = Memory<byte>.Empty });
        var response = Assert.Single(replies); Assert.Equal(5102u, response.Id);
        Assert.Equal((int)room.Id, response.Payload.ReadInt()); Assert.Equal(1, response.Payload.ReadInt()); Assert.Equal(0, response.Payload.ReadInt());
        Assert.Equal(2, store.Loads); Assert.Equal(store.Saved, settings.Snapshot);
        Assert.True(settings.TrySave(owner, 2, 2, "UTC", out _));
        Assert.Equal(new(2, 2, "UTC"), store.Saved);
    }

    [Fact]
    public async Task ActualSettingsReloadFailurePreservesSnapshotAndDeletedRowRestoresLegacyRights()
    {
        var room = Room(); var owner = Client(room, 1); var decorator = Client(room, 2); room.UsersWithRights.Add(2);
        var store = new MemoryStore { Saved = new(1, 0, "UTC") };
        var settings = Register(room, store); var replies = Capture(owner); var initial = settings.Snapshot;
        store.FailLoad = true;
        await new WiredRoomSettingsRequestEvent(Service()).Parse(room, owner, new FlashIncomingPacket { Buffer = Memory<byte>.Empty });
        Assert.Equal(156u, Assert.Single(replies).Id); Assert.Same(initial, settings.Snapshot);
        Assert.False(settings.CanModify(decorator)); Assert.Equal("UTC", settings.ExplicitTimeZone!.Id);
        store.FailLoad = false; store.Saved = null; replies.Clear();
        await new WiredRoomSettingsRequestEvent(Service()).Parse(room, owner, new FlashIncomingPacket { Buffer = Memory<byte>.Empty });
        Assert.Equal(5102u, Assert.Single(replies).Id); Assert.Equal(new(), settings.Snapshot);
        Assert.True(settings.CanModify(decorator)); Assert.Null(settings.ExplicitTimeZone);
        Assert.True(settings.TrySave(owner, 2, 2, "", out _)); // First-row CAS now expects absence.
    }

    [Fact]
    public void SettingsComposerMatchesAllActiveParserFieldsIncludingTimezone()
    {
        var room = Room(); var owner = Client(room, 1);
        var settings = new WiredRoomSettings(room, new MemoryStore { Saved = new(15, 14, "Europe/Berlin") });
        using var stream = PlusMemoryStream.GetStream(); var packet = new FlashOutgoingPacket(stream);
        var view = settings.View(owner);
        var captured = new HabbiconTestSupport.RecordingPacket();
        new WiredRoomSettingsDataComposer(view).Compose(captured);
        new WiredRoomSettingsDataComposer(view).Compose(packet);
        var read = new FlashIncomingPacket { Buffer = stream.ToArray().AsMemory(6) };
        Assert.Equal((int)room.Id, read.ReadInt()); Assert.Equal(15, read.ReadInt()); Assert.Equal(14, read.ReadInt());
        Assert.True(read.ReadBool()); Assert.True(read.ReadBool()); Assert.True(read.ReadBool());
        Assert.Equal("Europe/Berlin", read.ReadString()); Assert.False(read.HasDataRemaining());
        Assert.True(settings.TrySave(owner, 2, 2, "UTC", out _));
        var recomposed = new HabbiconTestSupport.RecordingPacket();
        new WiredRoomSettingsDataComposer(view).Compose(recomposed);
        Assert.Equal(captured.Writes, recomposed.Writes);
    }

    [Fact]
    public async Task SettingsHandlersDecodeExactPrimitiveFramesBeforeDelegating()
    {
        var room = Room();
        var session = Client(room, 1);
        var service = new RecordingService();

        await new WiredRoomSettingsRequestEvent(service).Parse(room, session, HabbiconTestSupport.Incoming());
        await new WiredRoomSettingsSaveEvent(service).Parse(room, session, HabbiconTestSupport.Incoming(3, 4));
        await new WiredMenuPermissionsSaveEvent(service).Parse(room, session, HabbiconTestSupport.Incoming(5, 6, "Europe/Berlin"));

        Assert.Equal((room, session), service.Reloaded);
        Assert.Equal((room, session, 3, 4, null), service.Saves[0]);
        Assert.Equal((room, session, 6, 5, "Europe/Berlin"), service.Saves[1]);

        await new WiredRoomSettingsSaveEvent(service).Parse(room, session, HabbiconTestSupport.Incoming(3, 4, 9));
        await new WiredMenuPermissionsSaveEvent(service).Parse(room, session, HabbiconTestSupport.Incoming(5, 6));
        Assert.Equal(2, service.Saves.Count);
    }

    [Theory]
    [InlineData("1.6.6.json")]
    [InlineData("example.json")]
    public void ActualRoomSettingsHandlersHaveUniqueActiveProfileMappings(string profile)
    {
        IPacketEvent[] handlers = [new WiredRoomSettingsRequestEvent(Service()), new WiredRoomSettingsSaveEvent(Service()), new WiredMenuPermissionsSaveEvent(Service())];
        using var manager = new PacketManager(handlers, NullLogger<PacketManager>.Instance);
        var registered = (Dictionary<uint, IPacketEvent>)typeof(PacketManager).GetField("_incomingPackets", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
        var revision = JsonSerializer.Deserialize<Revision>(File.ReadAllText(Path.Join(AppContext.BaseDirectory, "revisions", profile)))!;
        foreach (var handler in handlers)
        {
            var name = handler.GetType().Name; var id = (uint)typeof(ClientPacketHeader).GetField(name)!.GetRawConstantValue()!;
            Assert.Same(handler, registered[id]);
            Assert.Single(typeof(ClientPacketHeader).GetFields(), field => field.IsLiteral && field.GetRawConstantValue() is uint value && value == id);
            if (profile == "example.json" && handler is WiredMenuPermissionsSaveEvent)
            {
                Assert.False(revision.IncomingHeaders.ContainsKey(name));
                Assert.Equal(1936u, revision.IncomingHeaders["UpdateFloorPropertiesEvent"]);
                continue;
            }
            var wire = handler is WiredMenuPermissionsSaveEvent ? 1936u : id;
            Assert.Equal(wire, revision.IncomingHeaders[name]);
            Assert.Single(revision.IncomingHeaders, entry => entry.Value == wire);
        }
        Assert.Equal(5102u, revision.OutgoingHeaders[nameof(ServerPacketHeader.WiredRoomSettingsDataComposer)]);
        Assert.Single(revision.OutgoingHeaders, entry => entry.Value == 5102);
    }

    private static WiredRoomSettings Register(Room room, MemoryStore store)
    {
        var settings = new WiredRoomSettings(room, store);
        Set(room, "_wiredComponent", new Plus.HabboHotel.Rooms.Instance.WiredComponent(room, TestLogging.Logger,
            TimeProvider.System, TestRoomSettings.Empty, new FixedFactory(settings), TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty));
        return settings;
    }
    private static IWiredRoomSettingsService Service() => new WiredRoomSettingsService(TestLogging.For<WiredRoomSettingsService>());
    private static List<(uint Id, FlashIncomingPacket Payload)> Capture(FlashGameClient client)
    {
        var replies = new List<(uint, FlashIncomingPacket)>();
        client.Revision = new() { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint>
        { [ServerPacketHeader.WiredRoomSettingsDataComposer] = 5102, [ServerPacketHeader.WiredValidationErrorComposer] = 156 } };
        typeof(GameClient).GetProperty("SendCallback", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(client,
            (Func<System.Net.Sockets.SocketAsyncEventArgs, bool>)(args =>
            { replies.Add(((uint)FlashGameClient.DecodeInt16(args.MemoryBuffer.Slice(4, 2)), new() { Buffer = args.MemoryBuffer[6..].ToArray() })); return true; }));
        return replies;
    }
    private static Room Room()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 77; room.OwnerId = 1; room.OwnerName = "owner"; room.Type = "private"; room.UsersWithRights = [];
        room.Group = (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group));
        Set(room.Group, "_administrators", new List<int>()); Set(room.Group, "_members", new List<int>());
        return room;
    }
    private static FlashGameClient Client(Room room, int id, params string[] rights)
    {
        var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient);
        client.SetHabbo(new Habbo { Id = id, Username = id == room.OwnerId ? "owner" : "user" + id,
            CurrentRoom = room, Access = EditorTestSupport.Access([.. rights]) }); return client;
    }
    private static void Set(object obj, string name, object value) =>
        obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    private sealed class MemoryStore : IWiredRoomSettingsStore
    {
        public WiredRoomSettingsSnapshot? Saved;
        public Action? BeforeSave;
        public bool Fail;
        public bool FailLoad;
        public int Loads;
        public WiredRoomSettingsSnapshot? Load(uint roomId)
        {
            Loads++;
            if (FailLoad) throw new InvalidOperationException("Rejected read.");
            return Saved;
        }
        public void Save(uint roomId, int actorId, bool staff, WiredRoomSettingsSnapshot? expected, WiredRoomSettingsSnapshot settings)
        {
            BeforeSave?.Invoke();
            if (Fail || Saved != expected) throw new InvalidOperationException("Rejected storage.");
            Saved = settings;
        }
    }
    private sealed class FixedFactory(WiredRoomSettings settings) : IWiredRoomSettingsFactory
    {
        public WiredRoomSettings Create(Room room) => settings;
    }
    private sealed class RecordingService : IWiredRoomSettingsService
    {
        public (Room Room, GameClient Session)? Reloaded { get; private set; }
        public List<(Room Room, GameClient Session, int Inspect, int Modify, string? Timezone)> Saves { get; } = [];
        public void Reload(Room room, GameClient session) => Reloaded = (room, session);
        public void Save(Room room, GameClient session, int inspect, int modify, string? timezone) =>
            Saves.Add((room, session, inspect, modify, timezone));
    }
}
