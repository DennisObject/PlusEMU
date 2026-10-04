using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms.Engine;
using Plus.Communication.Packets.Incoming.Rooms.Furni.Stickys;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Plus.Database;
using Plus.Core.Settings;
using Plus.Database.Interfaces;
using Plus.HabboHotel;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

[CollectionDefinition("Placed furni room", DisableParallelization = true)]
public class PlacedFurniRoomCollection;

/// <summary>
/// Furniture placed from the inventory is built without a room id. The room handler must bind it,
/// or walking over it and opening a wired box dereference a null room.
/// </summary>
[Collection("Placed furni room")]
public partial class PlacedFurniRoomTests : IDisposable
{
    private const uint RoomId = 42;
    private readonly FieldInfo _gameField = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly FieldInfo _databaseField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly object? _previousGame;
    private readonly object? _previousDatabase;
    private readonly Room _room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
    private readonly TestClient _client = new();
    private readonly IDatabase _database;

    public PlacedFurniRoomTests()
    {
        _previousGame = _gameField.GetValue(null);
        _previousDatabase = _databaseField.GetValue(null);
        _room.Id = RoomId;
        _room.OwnerId = 7;
        _room.OwnerName = "owner";
        _room.Type = "private";
        Set("_gamemap", new Gamemap(_room, new RoomModel("test", 0, 0, 0, 0, "0000\r0000\r0000\r0000", 0, 0, false)));
        Set("_roomItemHandling", new RoomItemHandling(_room));
        Set("_roomUserManager", new RoomUserManager(_room));
        var wired = new WiredComponent(_room);
        typeof(WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(wired, new EmptyConfigurationStore());
        Set("_wiredComponent", wired);
        _room.GetGameMap().GenerateMaps();
        _client.SetHabbo(new Habbo { Id = 7, Username = "owner", CurrentRoom = _room, Access = UserAccess.Empty });

        var rooms = Proxy<IRoomManager>((method, args) =>
        {
            Assert.Equal("TryGetRoom", method);
            if ((uint)args[0]! != RoomId)
                return false;
            args[1] = _room;
            return true;
        });
        var clients = Proxy<IGameClientManager>((method, args) => method == "GetClientByUserId" && (int)args[0]! == 7 ? _client : null);
        _gameField.SetValue(null, Proxy<IGame>((method, _) => method switch
        {
            "get_RoomManager" => rooms,
            "get_ClientManager" => clients,
            _ => throw new InvalidOperationException(method)
        }));
        var query = Proxy<IQueryAdapter>((_, _) => null);
        _database = Proxy<IDatabase>((method, _) => method switch
        {
            "GetQueryReactor" => query,
            "Connection" => new NoOpConnection(),
            _ => throw new InvalidOperationException(method)
        });
        _databaseField.SetValue(null, _database);
    }

    [Fact]
    public void PlacedFloorItemIsWalkableWithoutLosingItsRoom()
    {
        var item = Furni(10, InteractionType.None, WiredBoxType.None);

        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, 1, 1, 0, true, false, false));
        Assert.Same(_room, item.GetRoom());

        var user = new RoomUser(7, RoomId, 1, _room);
        item.UserWalksOnFurni(user);
        item.UserWalksOffFurni(user);
        Assert.Same(item, user.LastItem);
    }

    [Fact]
    public void PlacedWallItemKeepsItsRoom()
    {
        var item = Furni(11, InteractionType.None, WiredBoxType.None, ItemType.Wall);

        Assert.True(_room.GetRoomItemHandler().SetWallItem(_client, item));
        Assert.Same(_room, item.GetRoom());
    }

    [Theory]
    [InlineData(InteractionType.WiredTrigger, WiredBoxType.TriggerWalkOnFurni, ServerPacketHeader.WiredTriggeRconfigComposer)]
    [InlineData(InteractionType.WiredEffect, WiredBoxType.EffectShowMessage, ServerPacketHeader.WiredEffectConfigComposer)]
    [InlineData(InteractionType.WiredCondition, WiredBoxType.ConditionFurniHasUsers, ServerPacketHeader.WiredConditionConfigComposer)]
    public void PlacedWiredBoxOpensItsEditor(InteractionType interaction, WiredBoxType type, uint composer)
    {
        var item = Furni(12, interaction, type);

        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, 2, 2, 0, true, false, false));
        _room.GetWired().LoadWiredBox(item);
        item.Interactor.OnTrigger(_client, item, 0, true);

        Assert.Equal(new[] { composer }, _client.Sent);
    }

    [Fact]
    public async Task PlacedInventoryFurniGoesBackToTheOwnersInventory()
    {
        // A traded item still carries the sender as OwnerId; the inventory holding it is the owner.
        Inventory(new InventoryItem { Id = 30, OwnerId = 99, Definition = Furni(30, InteractionType.None, WiredBoxType.None).Definition });

        await PlaceObject().Parse(_room, _client, ClientPacket("30 1 1 0"));
        var placed = _room.GetRoomItemHandler().GetItem(30);
        Assert.Null(_client.GetHabbo().Inventory.Furniture.GetItem(30));

        _client.Sent.Clear();
        await new PickupObjectEvent(Proxy<IGameClientManager>((_, _) => null), Proxy<IQuestManager>((_, _) => null), _database)
            .Parse(_client, ClientPacket(0, 30));

        Assert.Null(_room.GetRoomItemHandler().GetItem(30));
        Assert.NotNull(_client.GetHabbo().Inventory.Furniture.GetItem(30));
        Assert.Contains(ServerPacketHeader.FurniListUpdateComposer, _client.Sent);
        Assert.Equal((7, "owner", 7u), (placed.UserId, placed.Username, placed.OwnerId));
    }

    [Fact]
    public async Task PlacingAnItemNoLongerInTheInventoryIsIgnored()
    {
        Inventory(new InventoryItem { Id = 30, Definition = Furni(30, InteractionType.None, WiredBoxType.None).Definition });

        await PlaceObject().Parse(_room, _client, ClientPacket("31 1 1 0"));

        Assert.Empty(_room.GetRoomItemHandler().GetWallAndFloor);
        Assert.NotNull(_client.GetHabbo().Inventory.Furniture.GetItem(30));
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public async Task PlacedStickyNoteKeepsItsOwner()
    {
        var sticky = Furni(31, InteractionType.Postit, WiredBoxType.None, ItemType.Wall);
        Inventory(new InventoryItem { Id = 31, Definition = sticky.Definition });

        await new AddStickyNoteEvent().Parse(_room, _client, ClientPacket(31, ":w=1,1 l=0,0 l"));

        var placed = _room.GetRoomItemHandler().GetItem(31);
        Assert.Equal((7, "owner", 7u), (placed.UserId, placed.Username, placed.OwnerId));
    }

    private static Item Furni(uint id, InteractionType interaction, WiredBoxType wired, ItemType type = ItemType.Floor) => new()
    {
        Id = id,
        OwnerId = 7,
        Definition = new ItemDefinition
        {
            Type = type,
            SpriteId = 10,
            Height = 0.5,
            Modes = 1,
            Stackable = true,
            InteractionType = interaction,
            WiredType = wired,
            ItemName = "",
            PublicName = "",
            VendingIds = new List<int>(),
            AdjustableHeights = new List<double>()
        }
    };

    private static PlaceObjectEvent PlaceObject() =>
        new(Proxy<IRoomManager>((_, _) => null), Proxy<ISettingsManager>((_, _) => "500"), Proxy<IAchievementManager>((_, _) => null));

    private void Inventory(InventoryItem item) =>
        _client.GetHabbo().Inventory = new InventoryComponent
        {
            Furniture = new FurnitureInventoryComponent(item.IsFloorItem ? [item] : [], item.IsWallItem ? [item] : [])
        };

    /// <summary>A Flash packet body of big-endian ints and short-prefixed UTF-8 strings.</summary>
    private static FlashIncomingPacket ClientPacket(params object[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            if (value is bool flag)
                stream.WriteByte(flag ? (byte)1 : (byte)0);
            else if (value is string text)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                var length = new byte[2];
                BinaryPrimitives.WriteInt16BigEndian(length, (short)bytes.Length);
                stream.Write(length);
                stream.Write(bytes);
            }
            else
            {
                var bytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(bytes, (int)value);
                stream.Write(bytes);
            }
        }
        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    private void Set(string field, object value) =>
        typeof(Room).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_room, value);

    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Call = call;
        return proxy;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            Call(method!.Name, args!) ?? (method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null);
    }

    /// <summary>Accepts Dapper writes without a database.</summary>
    private sealed class NoOpConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "";
        public override string DataSource => "";
        public override string ServerVersion => "";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new NoOpCommand { Connection = this };
    }

    private sealed class NoOpCommand : DbCommand
    {
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new NoOpParameters();
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => 1;
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new NoOpParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    private sealed class NoOpParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull] public override string ParameterName { get; set; } = "";
        public override int Size { get; set; }
        [AllowNull] public override string SourceColumn { get; set; } = "";
        public override bool SourceColumnNullMapping { get; set; }
        public override object? Value { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class NoOpParameters : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];
        public override int Count => _items.Count;
        public override object SyncRoot => _items;
        public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
        public override void AddRange(Array values) { foreach (var value in values) Add(value); }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => IndexOf(value) >= 0;
        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);
        public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));
        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) => _items[IndexOf(parameterName)] = value;
    }

    private sealed class EmptyConfigurationStore : IWiredConfigurationStore
    {
        public WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor) => null;
        public void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration) { }
    }

    private sealed class TestClient : GameClient
    {
        public Action<uint>? BeforeCapture { get; set; }
        public List<uint> Sent { get; } = new();
        public List<(uint Header, byte[] Body)> Packets { get; } = new();
        public TestClient() : base(TestGameServer.Instance, new FlashPacketFactory())
        {
            Revision = new Revision
            {
                InternalIdToOutgoingIdMapping = typeof(ServerPacketHeader).GetFields()
                    .Where(field => field.IsLiteral && field.FieldType == typeof(uint))
                    .Select(field => (uint)field.GetRawConstantValue()!).Distinct().ToDictionary(id => id)
            };
            SendCallback = _ => false;
        }
        internal override (bool Complete, bool Malformed, uint MessageId, int HeaderLength, int Length) GetMessageIdAndPacketLength(ReadOnlyMemory<byte> buffer) =>
            (true, false, 0, 0, 0);
        public override void CreateHeader(Memory<byte> memory, uint messageId)
        {
            BeforeCapture?.Invoke(messageId);
            Sent.Add(messageId);
            Packets.Add((messageId, memory[6..].ToArray()));
        }
    }

    public void Dispose()
    {
        _gameField.SetValue(null, _previousGame);
        _databaseField.SetValue(null, _previousDatabase);
    }
}
