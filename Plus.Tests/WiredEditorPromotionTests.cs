using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Plus.Database;
using Plus.Core.FigureData;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Boxes.Conditions;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Items.Wired.Boxes.Triggers;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

[Collection("Modern Wired database seam")]
public class WiredEditorPromotionTests
{
    private static IWiredConfigurationService Service(IDatabase database, IFigureDataManager? figures = null) =>
        new WiredConfigurationService(new WiredConfigurationStore(database),
            figures ?? DispatchProxy.Create<IFigureDataManager, UnusedFigure>(), TestLogging.For<WiredConfigurationService>());

    public class UnusedFigure : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected figure lookup: {method?.Name}");
    }

    [Fact]
    public async Task SaveHandlersDecodeCompleteTypedRequestsAndRejectMalformedOrTrailingFrames()
    {
        var service = new RecordingConfigurationService();
        var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient);

        await new SaveWiredTriggerConfigEvent(service).Parse(client, Packet(0, [0, 1, 0], "trigger"));
        await new SaveWiredConditionConfigEvent(service).Parse(client, Packet(3, [2, 8, 0], "condition"));
        await new SaveWiredEffectConfigEvent(service).Parse(client, Packet(1, [0, 0, 34, -1], "action"));

        Assert.Collection(service.Requests,
            request => Assert.Equal((7u, WiredBoxCategory.Trigger, "trigger", 0),
                (request.ItemId, request.Envelope, request.Configuration.Text, request.Configuration.Delay)),
            request => Assert.Equal((7u, WiredBoxCategory.Condition, "condition", 0),
                (request.ItemId, request.Envelope, request.Configuration.Text, request.Configuration.Delay)),
            request => Assert.Equal((7u, WiredBoxCategory.Action, "action", 7),
                (request.ItemId, request.Envelope, request.Configuration.Text, request.Configuration.Delay)));

        await new SaveWiredEffectConfigEvent(service).Parse(client, new FlashIncomingPacket { Buffer = new byte[3] });
        var valid = Packet(1, [0, 0, 34, -1], "action").Buffer.ToArray();
        await new SaveWiredEffectConfigEvent(service).Parse(client,
            new FlashIncomingPacket { Buffer = valid.Concat(new byte[] { 1 }).ToArray() });
        Assert.Equal(3, service.Requests.Count);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task BotClothesSavesValidateMembershipBeforePersistingModernOrLegacyBoxes(bool legacy, bool member)
    {
        var database = new MemoryDatabase();
        var (room, wired, _) = Room();
        var item = new Item { Id = 7, ExtraData = new LegacyDataFormat { Data = "1" }, Definition = new()
            { ItemName = "wf_act_bot_clothes", WiredType = WiredBoxType.EffectBotChangesClothesBox, InteractionType = InteractionType.WiredEffect } };
        Floor(room).TryAdd(7, item);
        IWiredItem box = legacy
            ? new BotChangesClothesBox(room, item, TestBotManagementStore.Instance)
            : wired.CreateConfiguredBox(item)!;
        Assert.True(wired.AddBox(box));
        var packets = new List<uint>(); var client = SaveClient(room, packets);
        client.GetHabbo().Gender = "M";
        client.GetHabbo().Clothing = new Plus.HabboHotel.Users.Clothing.ClothingComponent();
        client.GetHabbo().Access = UserAccess.Create([], member ? [new(PermissionKeys.ClubAccess, false)] : []);
        var figures = DispatchProxy.Create<IFigureDataManager, SavingFigure>();
        await new SaveWiredEffectConfigEvent(Service(database, figures)).Parse(client, Packet(1, [100], "Bot\thd-10-20."));
        Assert.Equal(new uint[] { 1155 }, packets);
        Assert.Equal(member ? 2 : 0, ((SavingFigure)(object)figures).Level);
        Assert.True(wired.TryGet(7, out var saved));
        var configuration = Assert.IsAssignableFrom<IWiredConfiguredItem>(saved).Configuration;
        Assert.Equal(member ? "Bot\thd-10-20" : "Bot\thd-11-21", configuration.Text);
        Assert.Contains(configuration.Text.Replace("\t", "\\t"), database.Rows[7].Json);
    }
    public class SavingFigure : DispatchProxy
    {
        public int Level { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal("ProcessFigure", method!.Name);
            Assert.Equal("hd-10-20", args![0]); Assert.Equal("M", args[1]); Assert.NotNull(args[2]);
            Level = (int)args[3]!;
            return Level > 0 ? "hd-10-20." : "hd-11-21.";
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualSaveHandlerAllowsTemporaryTemplateCaptureOnlyForTypedPlaceAndPublishesAfterDurability(bool typed)
    {
        var database = new MemoryDatabase();
        var (room, wired, _) = Room();
        var map = new Gamemap(room, new RoomModel("template-test", 0, 0, 0, 0, "000\r000\r000", 0, 0, true), TestLogging.Navigation);
        Set(room, "_gamemap", map); typeof(Gamemap).GetProperty("GameMap")!.SetValue(map, new byte[3, 3]);
        typeof(Gamemap).GetProperty("EffectMap")!.SetValue(map, new byte[3, 3]);
        var item = new Item { Id = 7, ExtraData = new LegacyDataFormat { Data = "1" }, Definition = new()
            { ItemName = "wf_act_place_furni", Type = ItemType.Floor } };
        Floor(room).TryAdd(7, item);
        var box = wired.CreateConfiguredBox(item)!;
        var initial = new WiredConfiguration { IntParams = [32, 1, 0, 0, 0, 0],
            TemporaryPlacement = typed ? new(OffsetX: 1) : null, SecondarySelectedItems = [8],
            FurniSources = System.Collections.Immutable.ImmutableDictionary<string, int>.Empty.Add("target", 100) };
        Assert.True(box.TryValidateConfiguration(initial, out initial, out var error), error);
        box.ApplyConfiguration(initial); Assert.True(wired.AddBox(box));
        var prototype = room.GetRoomItemHandler().PlaceTemporaryFloorItem(new() { Id = 32, ItemName = "prototype", Type = ItemType.Floor,
            Length = 1, Width = 1, Stackable = true, Walkable = true }, 42, 1, 1, 0, 0, "prototype-state")!;
        Assert.True(room.GetRoomItemHandler().OwnsTemporary(prototype));
        var packets = new List<uint>(); var client = SaveClient(room, packets);
        var handler = new SaveWiredEffectConfigEvent(Service(database));
        database.FailWrites = true;
        await handler.Parse(client, ActionPacket([32, 2, 0, 0, 0, 0], [prototype.Id]));
        Assert.Equal(new uint[] { 156 }, packets); Assert.Same(initial, box.Configuration); Assert.Empty(database.Rows);
        database.FailWrites = false; packets.Clear();
        await handler.Parse(client, ActionPacket([32, 2, 0, 0, 0, 0], [prototype.Id]));
        if (!typed)
        {
            Assert.Equal(new uint[] { 156 }, packets); Assert.Same(initial, box.Configuration); Assert.Empty(database.Rows);
            return;
        }
        Assert.Equal(new uint[] { 1155 }, packets); Assert.Empty(box.Configuration.SelectedItems);
        Assert.Equal(new uint[] { 8 }, box.Configuration.SecondarySelectedItems);
        Assert.Equal(initial.TemporaryPlacement, box.Configuration.TemporaryPlacement);
        Assert.Equal(100, box.Configuration.FurniSources["target"]);
        var snapshot = Assert.Single(box.Configuration.Snapshots);
        Assert.Equal(prototype.Id, snapshot.ItemId); Assert.Equal(32u, snapshot.DefinitionId); Assert.Equal("prototype-state", snapshot.State);
        Assert.Equal((1, 1), (snapshot.X, snapshot.Y));
        Assert.Single(database.Rows); Assert.True(room.GetRoomItemHandler().RemoveTemporaryFloorItem(prototype));
        packets.Clear();
        await handler.Parse(client, ActionPacket([32, 3, 0, 0, 0, 0], []));
        Assert.Equal(new uint[] { 1155 }, packets); Assert.Equal(snapshot, Assert.Single(box.Configuration.Snapshots));
        Assert.Equal(initial.TemporaryPlacement, box.Configuration.TemporaryPlacement);
        Assert.All(database.Writes, sql => Assert.StartsWith("INSERT INTO wired_item_configurations", sql));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualStaticSaveAcceptsPermanentHighIdAndRejectsTemporaryIdentity(bool temporary)
    {
        var database = new MemoryDatabase(); var (room, wired, _) = Room();
        var item = new Item { Id = 7, ExtraData = new LegacyDataFormat { Data = "1" }, Definition = new()
            { ItemName = "wf_act_toggle_state", Type = ItemType.Floor } };
        Floor(room).TryAdd(7, item); var box = wired.CreateConfiguredBox(item)!; Assert.True(wired.AddBox(box));
        var high = new Item { Id = uint.MaxValue - 10, IsTemporary = temporary, Definition = new() { Type = ItemType.Floor } };
        Floor(room).TryAdd(high.Id, high);
        var prior = box.Configuration; var packets = new List<uint>();
        await new SaveWiredEffectConfigEvent(Service(database)).Parse(SaveClient(room, packets), ActionPacket([0, 100], [high.Id]));
        Assert.Equal(new uint[] { temporary ? 156u : 1155u }, packets);
        if (temporary) { Assert.Same(prior, box.Configuration); Assert.Empty(database.Rows); }
        else { Assert.Equal(new uint[] { high.Id }, box.Configuration.SelectedItems); Assert.Single(database.Rows); }
    }

    [Fact]
    public async Task SaveServiceRejectsMismatchedEnvelopeWithoutPersistenceOrSuccess()
    {
        var database = new MemoryDatabase(); var (room, wired, _) = Room();
        var item = new Item { Id = 7, ExtraData = new LegacyDataFormat { Data = "1" }, Definition = new()
            { ItemName = "wf_trg_says_something", WiredType = WiredBoxType.TriggerUserSays, InteractionType = InteractionType.WiredTrigger } };
        Floor(room).TryAdd(7, item); Assert.True(wired.AddBox(wired.GenerateNewBox(item)!));
        var packets = new List<uint>();

        await new SaveWiredEffectConfigEvent(Service(database)).Parse(SaveClient(room, packets),
            ActionPacket([0, 0, 34, -1], []));

        Assert.Equal(new uint[] { 156 }, packets);
        Assert.Empty(database.Rows);
        Assert.True(wired.TryGet(7, out var retained));
        Assert.IsNotAssignableFrom<IWiredConfiguredItem>(retained);
    }

    [Fact]
    public async Task ActualCurrentScoreSavePreservesNamedQuotaWithoutReinterpretingArithmeticFields()
    {
        var database = new MemoryDatabase(); var (room, wired, _) = Room();
        var item = new Item { Id = 7, ExtraData = new LegacyDataFormat { Data = "1" }, Definition = new()
            { ItemName = "wf_act_give_score", Type = ItemType.Floor } };
        Floor(room).TryAdd(7, item); var box = wired.CreateConfiguredBox(item)!;
        Assert.True(box.TryValidateConfiguration(new() { IntParams = [2, 0, 0], ScoreQuotaPerGame = 3 }, out var prior, out var error), error);
        box.ApplyConfiguration(prior); Assert.True(wired.AddBox(box));
        var packets = new List<uint>();
        await new SaveWiredEffectConfigEvent(Service(database)).Parse(SaveClient(room, packets), ActionPacket([4, 1, 0], []));
        Assert.Equal(new uint[] { 1155 }, packets); Assert.Equal(3, box.Configuration.ScoreQuotaPerGame);
        Assert.Equal(new[] { 4, 1, 0 }, box.Configuration.IntParams);
    }

    private static FlashGameClient SaveClient(Room room, List<uint> packets)
    {
        var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
        {
            Revision = new() { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint>
                { [ServerPacketHeader.WiredValidationErrorComposer] = 156, [ServerPacketHeader.HideWiredConfigComposer] = 1155 } },
            SendCallback = args => { packets.Add((uint)FlashGameClient.DecodeInt16(args.MemoryBuffer.Slice(4, 2))); return true; }
        };
        client.SetHabbo(new Habbo { Username = "owner", CurrentRoom = room, Access = EditorTestSupport.Access([]) }); return client;
    }

    private static FlashIncomingPacket ActionPacket(int[] parameters, uint[] selected)
    {
        using var stream = PlusMemoryStream.GetStream(); var packet = new FlashOutgoingPacket(stream);
        packet.WriteUInteger(7); packet.WriteInteger(parameters.Length); foreach (var value in parameters) packet.WriteInteger(value);
        packet.WriteString(""); packet.WriteInteger(selected.Length); foreach (var id in selected) packet.WriteUInteger(id);
        packet.WriteInteger(0); packet.WriteInteger(0); return new() { Buffer = stream.ToArray().AsMemory(6) };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RewardStaffAuthorizationUsesCanonicalDescriptorEvenWhenLegacyTypeAndWiredIdAreZero(bool staff)
    {
        var (room, wired, _) = Room();
        var item = new Item { Id = 7, Definition = new() { ItemName = "custom_name", WiredType = WiredBoxType.None } };
        Floor(room).TryAdd(7, item);
        var box = new RewardValidationProbe(room, item); Assert.True(wired.AddBox(box));
        string? error = null;
        var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
        {
            Revision = new() { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint> { [ServerPacketHeader.WiredValidationErrorComposer] = 156 } },
            SendCallback = args => { error = new FlashIncomingPacket { Buffer = args.MemoryBuffer[6..].ToArray() }.ReadString(); return true; }
        };
        client.SetHabbo(new Habbo { Username = "owner", CurrentRoom = room, Access = EditorTestSupport.Access(staff ? ["moderation.tool"] : []) });
        await new SaveWiredEffectConfigEvent(Service(new MemoryDatabase())).Parse(client, Packet(1, [0, 0, 0, 1, 0], ""));
        Assert.Equal(staff, box.Validated);
        Assert.Equal(staff ? "Rejected by concrete reward validation." : "You do not have permission to configure Wired rewards.", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ActualSaveHandlerPublishesOnlyDurableCurrentFieldsAndReloadsWithoutRecapture(int kind)
    {
        var database = new MemoryDatabase();
        var field = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;
        var prior = field.GetValue(null); field.SetValue(null, database);
        try
        {
            var (room, wired, target) = Room();
            var names = new[] { "wf_trg_says_something", "wf_act_show_message", "wf_act_teleport_to", "wf_cnd_user_count_in", "wf_act_match_to_sshot" };
            var types = new[] { WiredBoxType.TriggerUserSays, WiredBoxType.EffectShowMessage, WiredBoxType.EffectTeleportToFurni, WiredBoxType.ConditionUserCountInRoom, WiredBoxType.EffectMatchPosition };
            var item = new Item { Id = 7, ExtraData = new LegacyDataFormat { Data = "1" }, Definition = new()
                { ItemName = names[kind], WiredType = types[kind], InteractionType = kind == 0 ? InteractionType.WiredTrigger
                    : kind == 3 ? InteractionType.WiredCondition : InteractionType.WiredEffect } };
            Floor(room).TryAdd(item.Id, item);
            var original = wired.GenerateNewBox(item)!;
            original.StringData = kind switch { 3 => "1;5", 4 => "0;0;0", _ => "old bytes" };
            original.BoolData = true; original.ItemsData = "8:0,0,1,0,old-snapshot;";
            Assert.True(wired.AddBox(original));
            var oldText = original.StringData; var oldSelected = original.SetItems;
            var packets = new List<uint>();
            var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
            {
                Revision = new() { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint>
                    { [ServerPacketHeader.WiredValidationErrorComposer] = 156, [ServerPacketHeader.HideWiredConfigComposer] = 1155 } },
                SendCallback = args => { packets.Add((uint)FlashGameClient.DecodeInt16(args.MemoryBuffer.Slice(4, 2))); return true; }
            };
            client.SetHabbo(new Habbo { Username = "owner", CurrentRoom = room, Access = EditorTestSupport.Access([]) });
            var handler = kind == 0 ? (SaveWiredConfigEvent)new SaveWiredTriggerConfigEvent(Service(database))
                : kind == 3 ? new SaveWiredConditionConfigEvent(Service(database)) : new SaveWiredEffectConfigEvent(Service(database));
            var parameters = kind switch { 0 => new[] { 0, 1, 0 }, 1 => [0, 0, 34, -1], 2 => [0, 0, 0], 3 => [2, 8, 0], _ => [1, 1, 1, 1, 100] };
            var text = kind is 0 or 1 ? "new text" : "";
            database.FailWrites = true;
            await handler.Parse(client, Packet(kind, parameters, text));
            Assert.Equal(new uint[] { 156 }, packets);
            Assert.Empty(database.Rows);
            Assert.True(wired.TryGet(7, out var afterFailure)); Assert.Same(original, afterFailure);
            Assert.Equal(oldText, original.StringData); Assert.True(original.BoolData);
            Assert.Same(oldSelected, original.SetItems); Assert.Equal("8:0,0,1,0,old-snapshot;", original.ItemsData);

            database.FailWrites = false; packets.Clear();
            await handler.Parse(client, Packet(kind, parameters, text));
            Assert.Equal(new uint[] { 1155 }, packets);
            Assert.True(wired.TryGet(7, out var published));
            var configured = Assert.IsAssignableFrom<IWiredConfiguredItem>(published);
            Assert.NotSame(original, configured); Assert.Equal(names[kind], configured.Descriptor.CanonicalName);
            Assert.Equal(parameters, configured.Configuration.IntParams); Assert.Equal(text, configured.Configuration.Text);
            Assert.Single(database.Rows);
            Assert.All(database.Writes, sql => Assert.StartsWith("INSERT INTO wired_item_configurations", sql));
            Assert.Equal(oldText, original.StringData); Assert.Equal("8:0,0,1,0,old-snapshot;", original.ItemsData);
            if (kind == 2) Assert.Equal(0, configured.Configuration.FurniSources["targets"]); // Explicit trigger source survives saved picks.
            var savedSnapshot = kind == 4 ? Assert.Single(configured.Configuration.Snapshots) : null;
            target.LegacyDataString = "after-save";
            target.SetState(5, 6, 7, new());
            var reloaded = Assert.IsAssignableFrom<IWiredConfiguredItem>(new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestWiredRoomSettingsFactory.Instance, new WiredConfigurationStore(database), database, TestWiredRewardService.Instance, TestBotManagementStore.Instance).LoadWiredBox(item));
            Assert.Equal(parameters, reloaded.Configuration.IntParams);
            if (savedSnapshot != null) Assert.Equal(savedSnapshot, Assert.Single(reloaded.Configuration.Snapshots));
        }
        finally { field.SetValue(null, prior); }
    }

    private static FlashIncomingPacket Packet(int kind, int[] values, string text)
    {
        using var stream = PlusMemoryStream.GetStream(); var packet = new FlashOutgoingPacket(stream);
        packet.WriteUInteger(7); packet.WriteInteger(values.Length); foreach (var value in values) packet.WriteInteger(value);
        packet.WriteString(text); packet.WriteInteger(kind is 2 or 4 ? 1 : 0);
        if (kind is 2 or 4) packet.WriteUInteger(8);
        if (kind is not (0 or 3)) packet.WriteInteger(7);
        packet.WriteInteger(0);
        return new() { Buffer = stream.ToArray().AsMemory(6) };
    }

    private static (Room, WiredComponent, Item) Room()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 1; room.OwnerId = 42; room.OwnerName = "owner"; room.Type = "private";
        Set(room, "_roomItemHandling", new RoomItemHandling(room, TestRoomItemStore.Instance)); Set(room, "_roomUserManager", new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        var wired = new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance); Set(room, "_wiredComponent", wired);
        var target = new Item { Id = 8, ExtraData = new LegacyDataFormat { Data = "captured-state" }, Definition = new() { Id = 18, Type = ItemType.Floor } };
        target.SetState(1, 2, 3.5, new()); Floor(room).TryAdd(8, target);
        return (room, wired, target);
    }

    private static void Set(object value, string field, object data) => value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, data);
    private static ConcurrentDictionary<uint, Item> Floor(Room room) => (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;

    private sealed class MemoryDatabase : IDatabase
    {
        public bool FailWrites;
        public Dictionary<uint, (string Name, int Version, string Json)> Rows = [];
        public List<string> Writes = [];
        public bool IsConnected() => true;
        public IDbConnection Connection()
        {
            var state = ConnectionState.Closed;
            return Proxy.Create<IDbConnection>((method, _) => method.Name switch
            {
                "get_State" => state,
                "get_ConnectionString" => "wired-editor-memory",
                "Open" => Change(ConnectionState.Open), "Close" or "Dispose" => Change(ConnectionState.Closed),
                "CreateCommand" => Command(), _ => throw new NotSupportedException(method.Name)
            });
            object? Change(ConnectionState next) { state = next; return null; }
        }
        private IDbCommand Command()
        {
            var parameters = new MySqlCommand().Parameters; var sql = "";
            return Proxy.Create<IDbCommand>((method, args) =>
            {
                switch (method.Name)
                {
                    case "set_CommandText": sql = (string)args![0]!; return null;
                    case "get_CommandText": return sql;
                    case "get_Parameters": return parameters;
                    case "CreateParameter": return new MySqlParameter();
                    case "Dispose": case "set_CommandTimeout": case "set_CommandType": return null;
                    case "ExecuteNonQuery":
                        if (FailWrites) throw new IOException("Database write failed.");
                        var id = Convert.ToUInt32(parameters["Id"].Value);
                        Rows[id] = ((string)parameters["Name"].Value!, Convert.ToInt32(parameters["Version"].Value), (string)parameters["Configuration"].Value!);
                        Writes.Add(sql); return 1;
                    case "ExecuteReader":
                        var table = new DataTable(); table.Columns.Add("BoxName", typeof(string)); table.Columns.Add("Version", typeof(int)); table.Columns.Add("Json", typeof(string));
                        if (Rows.TryGetValue(Convert.ToUInt32(parameters["Id"].Value), out var row)) table.Rows.Add(row.Name, row.Version, row.Json);
                        return table.CreateDataReader();
                    default: throw new NotSupportedException(method.Name);
                }
            });
        }
    }

    private sealed class RewardValidationProbe(Room room, Item item) : IWiredConfiguredItem
    {
        public Room Instance { get; set; } = room;
        public Item Item { get; set; } = item;
        public WiredBoxType Type => WiredBoxType.None;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public WiredBoxDescriptor Descriptor { get; } = WiredBoxRegistry.All.Single(entry => entry.CanonicalName == "wf_act_give_reward") with { Support = WiredBoxSupport.Implemented };
        public WiredConfiguration Configuration { get; private set; } = new();
        public bool Validated;
        public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
        { Validated = true; validated = proposed; error = "Rejected by concrete reward validation."; return false; }
        public void ApplyConfiguration(WiredConfiguration configuration) => Configuration = configuration;
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] parameters) => throw new NotSupportedException();
    }

    public class Proxy : DispatchProxy
    {
        private Func<MethodInfo, object?[]?, object?> _invoke = null!;
        public static T Create<T>(Func<MethodInfo, object?[]?, object?> invoke) where T : class
        { var value = Create<T, Proxy>(); ((Proxy)(object)value)._invoke = invoke; return value; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => _invoke(method!, args);
    }

    private sealed class RecordingConfigurationService : IWiredConfigurationService
    {
        public List<WiredConfigurationSaveRequest> Requests { get; } = [];
        public void Save(GameClient session, WiredConfigurationSaveRequest request) => Requests.Add(request);
    }
}
