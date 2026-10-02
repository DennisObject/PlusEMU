using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Plus.Database;
using Plus.Database.Interfaces;
using Plus.HabboHotel;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
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
public class PlacedFurniRoomTests : IDisposable
{
    private const uint RoomId = 42;
    private readonly FieldInfo _gameField = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly FieldInfo _databaseField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly object? _previousGame;
    private readonly object? _previousDatabase;
    private readonly Room _room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
    private readonly TestClient _client = new();

    public PlacedFurniRoomTests()
    {
        _previousGame = _gameField.GetValue(null);
        _previousDatabase = _databaseField.GetValue(null);
        _room.Id = RoomId;
        _room.OwnerId = 7;
        _room.OwnerName = "owner";
        _room.Type = "private";
        Set("_gamemap", new Gamemap(_room, new RoomModel("test", 0, 0, 0, 0, "0000\r0000\r0000\r0000", false, 0, false)));
        Set("_roomItemHandling", new RoomItemHandling(_room));
        Set("_roomUserManager", new RoomUserManager(_room));
        Set("_wiredComponent", new WiredComponent(_room));
        _client.SetHabbo(new Habbo { Id = 7, Username = "owner", CurrentRoom = _room });

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
        _databaseField.SetValue(null, Proxy<IDatabase>((method, _) => method == "GetQueryReactor" ? query : throw new InvalidOperationException(method)));
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

    [Theory]
    [InlineData(InteractionType.WiredTrigger, WiredBoxType.TriggerWalkOnFurni, 1, new int[0])]
    [InlineData(InteractionType.WiredTrigger, WiredBoxType.TriggerRepeat, 6, new[] { 0 })]
    [InlineData(InteractionType.WiredEffect, WiredBoxType.EffectToggleFurniState, 0, new int[0])]
    [InlineData(InteractionType.WiredEffect, WiredBoxType.EffectTeleportToFurni, 8, new int[0])]
    [InlineData(InteractionType.WiredEffect, WiredBoxType.EffectMoveFurniToNearestUser, 11, new int[0])]
    [InlineData(InteractionType.WiredEffect, WiredBoxType.EffectShowMessage, 7, new int[0])]
    [InlineData(InteractionType.WiredEffect, WiredBoxType.EffectBotGivesHanditemBox, 24, new[] { 0 })]
    [InlineData(InteractionType.WiredEffect, WiredBoxType.EffectMatchPosition, 3, new[] { 0, 0, 0 })]
    [InlineData(InteractionType.WiredEffect, WiredBoxType.EffectMoveAndRotate, 4, new[] { 0, 0 })]
    [InlineData(InteractionType.WiredCondition, WiredBoxType.ConditionMatchStateAndPosition, 0, new[] { 0, 0, 0 })]
    [InlineData(InteractionType.WiredCondition, WiredBoxType.ConditionDontMatchStateAndPosition, 13, new[] { 0, 0, 0 })]
    [InlineData(InteractionType.WiredCondition, WiredBoxType.ConditionFurniHasUsers, 1, new int[0])]
    [InlineData(InteractionType.WiredCondition, WiredBoxType.ConditionUserCountInRoom, 5, new[] { 0, 0 })]
    public void PlacedWiredBoxSendsTheEditorTheClientReads(InteractionType interaction, WiredBoxType type, int code, int[] ints)
    {
        var editor = Open(Place(12, interaction, type));

        Assert.Equal(code, editor.Code);
        Assert.Equal(ints, editor.Ints);
        Assert.Equal(12, editor.Id);
    }

    [Fact]
    public void DelayedEffectReopensWithItsFurniAndDelay()
    {
        var picked = Furni(20, InteractionType.None, WiredBoxType.None);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, picked, 1, 1, 0, true, false, false));
        var item = Place(12, InteractionType.WiredEffect, WiredBoxType.EffectTeleportToFurni);
        var box = Box(item);
        box.SetItems.TryAdd(picked.Id, picked);
        ((IWiredCycle)box).Delay = 5;

        var editor = Open(item);

        Assert.Equal(8, editor.Code);
        Assert.Equal(5, editor.Delay);
        Assert.Equal(new[] { 20 }, editor.Stuffs);
    }

    [Fact]
    public void BotHandItemReopensWithTheHandItem()
    {
        var item = Place(12, InteractionType.WiredEffect, WiredBoxType.EffectBotGivesHanditemBox);
        Box(item).StringData = "Frank;7";

        var editor = Open(item);

        Assert.Equal(24, editor.Code);
        Assert.Equal(new[] { 7 }, editor.Ints);
        Assert.Equal("Frank", editor.Text);
    }

    [Fact]
    public void MatchStateConditionReopensWithItsOptions()
    {
        var picked = Furni(20, InteractionType.None, WiredBoxType.None);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, picked, 1, 1, 0, true, false, false));
        var item = Place(12, InteractionType.WiredCondition, WiredBoxType.ConditionMatchStateAndPosition);
        var box = Box(item);
        box.SetItems.TryAdd(picked.Id, picked);
        box.StringData = "1;0;1";

        var editor = Open(item);

        Assert.Equal(0, editor.Code);
        Assert.Equal(new[] { 1, 0, 1 }, editor.Ints);
        Assert.Equal(new[] { 20 }, editor.Stuffs);
    }

    private Item Place(uint id, InteractionType interaction, WiredBoxType type)
    {
        var item = Furni(id, interaction, type);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, 2, 2, 0, true, false, false));
        _room.GetWired().LoadWiredBox(item);
        return item;
    }

    private IWiredItem Box(Item item)
    {
        Assert.True(_room.GetWired().TryGet(item.Id, out var box));
        return box;
    }

    private Editor Open(Item item)
    {
        _client.Bodies.Clear();
        item.Interactor.OnTrigger(_client, item, 0, true);
        return Editor.Read(item.Definition.InteractionType, Assert.Single(_client.Bodies));
    }

    /// <summary>Reads a wired editor packet in the order of the Octane Triggerable and its definitions.</summary>
    private sealed record Editor(int Id, int[] Stuffs, string Text, int[] Ints, int Code, int Delay)
    {
        public static Editor Read(InteractionType interaction, byte[] body)
        {
            var at = 0;
            int Int() { var value = BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(at)); at += 4; return value; }
            int[] Ints() => Enumerable.Range(0, Int()).Select(_ => Int()).ToArray();
            at++; // stuff type selection enabled
            Int(); // furni limit
            var stuffs = Ints();
            Int(); // sprite
            var id = Int();
            var length = BinaryPrimitives.ReadInt16BigEndian(body.AsSpan(at));
            var text = Encoding.UTF8.GetString(body, at + 2, length);
            at += 2 + length;
            var ints = Ints();
            Int(); // stuff type selection code
            var code = Int();
            var delay = 0;
            if (interaction == InteractionType.WiredEffect)
            {
                delay = Int();
                Ints();
            }
            else if (interaction == InteractionType.WiredTrigger)
                Ints();
            Assert.Equal(body.Length, at);
            return new(id, stuffs, text, ints, code, delay);
        }
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
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args!);
    }

    private sealed class TestClient : GameClient
    {
        public List<uint> Sent { get; } = new();
        public List<byte[]> Bodies { get; } = new();
        public TestClient() : base(null!, new FlashPacketFactory())
        {
            Revision = new Revision
            {
                InternalIdToOutgoingIdMapping = typeof(ServerPacketHeader).GetFields()
                    .Where(field => field.IsLiteral && field.FieldType == typeof(uint))
                    .Select(field => (uint)field.GetRawConstantValue()!).Distinct().ToDictionary(id => id)
            };
            // Flash packets reserve a 6 byte header in front of the body.
            SendCallback = args =>
            {
                Bodies.Add(args.MemoryBuffer.Slice(6).ToArray());
                return false;
            };
        }
        internal override (bool Complete, bool Malformed, uint MessageId, int HeaderLength, int Length) GetMessageIdAndPacketLength(ReadOnlyMemory<byte> buffer) =>
            (true, false, 0, 0, 0);
        public override void CreateHeader(Memory<byte> memory, uint messageId) => Sent.Add(messageId);
    }

    public void Dispose()
    {
        _gameField.SetValue(null, _previousGame);
        _databaseField.SetValue(null, _previousDatabase);
    }
}
