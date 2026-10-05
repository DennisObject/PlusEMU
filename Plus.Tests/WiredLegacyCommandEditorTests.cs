using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.Communication.Revisions;
using Plus.HabboHotel;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Boxes.Triggers;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat;
using Plus.HabboHotel.Rooms.Chat.Commands;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

[Collection("Modern Wired database seam")]
public class WiredLegacyCommandEditorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentEditorSaveRetainsRealCommandDispatchOwnershipAndAcceptedFeedback(bool ownerOnly)
    {
        using var world = new World(ownerOnly);
        Assert.True(WiredLegacyEditorProjection.TryGetConfiguration(world.Box, out var descriptor, out var configuration));
        Assert.Equal(WiredBoxCategory.Trigger, descriptor.Envelope);
        Assert.Equal(0, descriptor.EditorCode);
        Assert.Equal(new[] { 0, 1, ownerOnly ? 1 : 0 }, configuration.IntParams);
        using var stream = PlusMemoryStream.GetStream();
        var composer = new WiredConfiguredConfigComposer(WiredEditorSnapshot.Capture(world.Box.Item, descriptor, configuration, 0));
        Assert.Equal(ServerPacketHeader.WiredTriggeRconfigComposer, composer.MessageId);
        composer.Compose(new FlashOutgoingPacket(stream));
        var opened = new FlashIncomingPacket { Buffer = stream.ToArray().AsMemory(6) };
        Assert.False(opened.ReadBool());
        Assert.Equal(0, opened.ReadInt()); // No furniture input for a command trigger.
        Assert.Equal(0, opened.ReadInt());
        Assert.Equal(91, opened.ReadInt()); Assert.Equal(7u, opened.ReadUInt());
        Assert.Equal(":first", opened.ReadString()); Assert.Equal(3, opened.ReadInt());
        Assert.Equal(0, opened.ReadInt()); Assert.Equal(1, opened.ReadInt());
        Assert.Equal(ownerOnly ? 1 : 0, opened.ReadInt());
        Assert.Equal(0, opened.ReadInt()); Assert.Equal(0, opened.ReadInt()); Assert.Equal(0, opened.ReadInt());
        Assert.False(opened.HasDataRemaining());

        await world.Command(world.Owner, ":first", true);
        await world.Command(world.Guest, ":first", !ownerOnly);
        await world.Command(world.Owner, ":second", false);
        var proposed = configuration with { Text = ":second" };
        Assert.True(WiredLegacyCustomEditor.TryPrepare(world.Box, proposed, WiredLegacyCustomEditor.CreateCandidate,
            out var candidate, out _));
        Assert.IsType<UserSaysCommandBox>(candidate);
        Assert.Equal(WiredBoxType.TriggerUserSaysCommand, candidate!.Type);
        Assert.Equal(":first", world.Box.StringData);
        Assert.Throws<IOException>(() => world.Wired.PublishLegacy(world.Box, candidate,
            () => throw new IOException("Database write failed.")));
        Assert.Equal(":first", world.Box.StringData); Assert.Equal(ownerOnly, world.Box.BoolData);
        await world.Command(world.Owner, ":first", true);
        await world.Command(world.Owner, ":second", false);

        var persisted = false;
        Assert.True(world.Wired.PublishLegacy(world.Box, candidate, () =>
        {
            Assert.Equal(":first", world.Box.StringData);
            Assert.Equal(":second", candidate.StringData); Assert.Equal(ownerOnly, candidate.BoolData);
            persisted = true;
        }));
        Assert.True(persisted);
        Assert.True(world.Wired.TryGet(world.Box.Item.Id, out var registered));
        Assert.Same(world.Box, registered);
        Assert.IsType<UserSaysCommandBox>(registered);
        Assert.Equal("legacy snapshot bytes", world.Box.ItemsData);
        await world.Command(world.Owner, ":first", false);
        await world.Command(world.Owner, ":second", true);
        await world.Command(world.Guest, ":second", !ownerOnly);
        world.Owner.WiredInteraction = false;
        Assert.False(world.Wired.TriggerEvent(WiredBoxType.TriggerUserSays, world.Owner, ":second"));
        Assert.False(world.Owner.WiredInteraction);
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 2)]
    public void UnsupportedCurrentCommandSettingsRejectBeforeDetachedFactory(int match, int hide, int owner)
    {
        var box = new UserSaysCommandBox(null!, new Item()) { StringData = ":first", BoolData = true };
        Assert.False(WiredLegacyCustomEditor.TryPrepare(box, new() { IntParams = [match, hide, owner], Text = ":second" },
            _ => throw new Exception("Factory must not run."), out _, out var error));
        Assert.Contains("Command Wired", error);
        Assert.Equal(":first", box.StringData); Assert.True(box.BoolData);
    }

    private sealed class World : IDisposable
    {
        private readonly FieldInfo _gameField = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? _previousGame;
        private readonly CommandManager _commands;
        private readonly Dictionary<int, List<byte[]>> _packets = [];
        public Room Room { get; }
        public WiredComponent Wired { get; }
        public UserSaysCommandBox Box { get; }
        public Habbo Owner { get; }
        public Habbo Guest { get; }

        public World(bool ownerOnly)
        {
            _previousGame = _gameField.GetValue(null);
            Room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)); Room.OwnerId = 42;
            var items = new RoomItemHandling(Room, TestRoomItemStore.Instance); var users = new RoomUserManager(Room, TestRoomUserStore.Instance, TimeProvider.System);
            Set(Room, "_roomItemHandling", items); Set(Room, "_roomUserManager", users);
            Wired = new WiredComponent(Room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty); Set(Room, "_wiredComponent", Wired);
            _commands = new CommandManager([new Command("first"), new Command("second")], null!, null!, new FixedTimeProvider(FixedTimeProvider.Epoch));
            var chat = new ChatManager(null!, _commands, null!, null!, null!, null!, null!, null!);
            var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game)); Set(game, "_chatManager", chat);
            _gameField.SetValue(null, game);
            Owner = AddUser(42, 1, users); Guest = AddUser(43, 2, users);
            var item = new Item { Id = 7, ExtraData = new LegacyDataFormat { Data = "1" }, Definition = new()
                { ItemName = "wf_trg_says_something", SpriteId = 91, WiredType = WiredBoxType.TriggerUserSaysCommand,
                  InteractionType = InteractionType.WiredTrigger } };
            ((ConcurrentDictionary<uint, Item>)Get(items, "_floorItems")).TryAdd(item.Id, item);
            Box = new UserSaysCommandBox(Room, item) { StringData = ":first", BoolData = ownerOnly, ItemsData = "legacy snapshot bytes" };
            Assert.True(Wired.AddBox(Box));
        }

        private Habbo AddUser(int id, int virtualId, RoomUserManager users)
        {
            var packets = new List<byte[]>(); _packets[id] = packets;
            var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
            {
                Revision = new() { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint> { [ServerPacketHeader.WhisperComposer] = 100 } },
                SendCallback = args => { packets.Add(args.MemoryBuffer.ToArray()); return true; }
            };
            var habbo = new Habbo { Id = id, Username = "actor" + id, CurrentRoom = Room, Client = client, Access = UserAccess.Create([], [new("command.first", false), new("command.second", false)], ["command.first", "command.second"]) };
            client.SetHabbo(habbo);
            var user = new RoomUser(id, 0, virtualId, Room); Set(user, "_mClient", client);
            ((ConcurrentDictionary<int, RoomUser>)Get(users, "_users")).TryAdd(virtualId, user);
            return habbo;
        }

        public async Task Command(Habbo actor, string command, bool accepted)
        {
            actor.WiredInteraction = false; _packets[actor.Id].Clear();
            Assert.True(await _commands.Parse(actor.Client, command));
            Assert.Equal(accepted, actor.WiredInteraction);
            if (!accepted) { Assert.Empty(_packets[actor.Id]); return; }
            var payload = Assert.Single(_packets[actor.Id]);
            var packet = new FlashIncomingPacket { Buffer = payload.AsMemory(6) };
            Assert.Equal(actor.Id == 42 ? 1 : 2, packet.ReadInt());
            Assert.Equal(command, packet.ReadString()); Assert.Equal(0, packet.ReadInt()); Assert.Equal(0, packet.ReadInt());
        }

        public void Dispose() => _gameField.SetValue(null, _previousGame);
        private static void Set(object value, string field, object data) => value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, data);
        private static object Get(object value, string field) => value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    }

    private sealed class Command(string key) : IChatCommand
    {
        public string Key => key;
        public string Parameters => "";
        public string Description => "Command editor regression fixture";
        public void Execute(GameClient session, Room room, string[] parameters) { }
    }
}
