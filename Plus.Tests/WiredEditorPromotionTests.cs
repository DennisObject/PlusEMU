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
using Plus.Database.Interfaces;
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
        var client = new FlashGameClient(null!, new FlashPacketFactory())
        {
            Revision = new() { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint> { [ServerPacketHeader.WiredValidationErrorComposer] = 156 } },
            SendCallback = args => { error = new FlashIncomingPacket { Buffer = args.MemoryBuffer[6..].ToArray() }.ReadString(); return true; }
        };
        client.SetHabbo(new Habbo { Username = "owner", CurrentRoom = room, Permissions = new(staff ? ["mod_tool"] : [], []) });
        await new SaveWiredEffectConfigEvent(new MemoryDatabase()).Parse(client, Packet(1, [0, 0, 0, 1, 0], ""));
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
            var client = new FlashGameClient(null!, new FlashPacketFactory())
            {
                Revision = new() { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint>
                    { [ServerPacketHeader.WiredValidationErrorComposer] = 156, [ServerPacketHeader.HideWiredConfigComposer] = 1155 } },
                SendCallback = args => { packets.Add((uint)FlashGameClient.DecodeInt16(args.MemoryBuffer.Slice(4, 2))); return true; }
            };
            client.SetHabbo(new Habbo { Username = "owner", CurrentRoom = room, Permissions = new([], []) });
            var handler = kind == 0 ? (SaveWiredConfigEvent)new SaveWiredTriggerConfigEvent(database)
                : kind == 3 ? new SaveWiredConditionConfigEvent(database) : new SaveWiredEffectConfigEvent(database);
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
            var reloaded = Assert.IsAssignableFrom<IWiredConfiguredItem>(new WiredComponent(room).LoadWiredBox(item));
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
        Set(room, "_roomItemHandling", new RoomItemHandling(room)); Set(room, "_roomUserManager", new RoomUserManager(room));
        var wired = new WiredComponent(room); Set(room, "_wiredComponent", wired);
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
        public IQueryAdapter GetQueryReactor() => throw new Exception("Promoted reload must use the companion row.");
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
}
