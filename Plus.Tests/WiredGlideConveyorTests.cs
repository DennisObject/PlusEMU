using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Plus.Communication.Flash;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

// Swaps the room engine's clock; shares the wired collection with the other engine fixtures.
[Collection("Modern Wired database seam")]
public sealed class WiredGlideConveyorTests
{
    // Room 14 as dumped live on 2026-10-07: id, x, y, z, rotation, name, saved configuration.
    private static readonly (uint Id, int X, int Y, double Z, int Rot, string Name, string? Json)[] Room14 =
    [
        (91, 4, 11, 0.00, 0, "wf_trg_period_short", """{"Version":1,"IntParams":[4],"Text":"","SelectedItems":[],"Delay":0,"SelectionCode":0,"ScoreQuotaPerGame":null,"TemporaryPlacement":null,"SecondarySelectedItems":[],"FurniSources":{},"UserSources":{},"VariableIds":[],"Snapshots":[]}"""),
        (107, 4, 11, 0.65, 2, "wf_act_move_to_dir", """{"Version":1,"IntParams":[2,0,100,1],"Text":"","SelectedItems":[115,114,116,117,118,119],"Delay":0,"SelectionCode":0,"ScoreQuotaPerGame":null,"TemporaryPlacement":null,"SecondarySelectedItems":[],"FurniSources":{"items":100},"UserSources":{},"VariableIds":[],"Snapshots":[]}"""),
        (108, 4, 11, 1.30, 0, "wf_act_send_signal", """{"Version":1,"IntParams":[113,100,0,0,0,0],"Text":"","SelectedItems":[113],"Delay":0,"SelectionCode":0,"ScoreQuotaPerGame":null,"TemporaryPlacement":null,"SecondarySelectedItems":[],"FurniSources":{"forwarded":100,"items":100},"UserSources":{"users":0},"VariableIds":[],"Snapshots":[]}"""),
        (110, 4, 11, 1.95, 0, "wf_xtra_mov_physics", """{"Version":1,"IntParams":[1,0,0,0,0,0,0],"Text":"","SelectedItems":[],"Delay":0,"SelectionCode":0,"ScoreQuotaPerGame":null,"TemporaryPlacement":null,"SecondarySelectedItems":[],"FurniSources":{},"UserSources":{},"VariableIds":[],"Snapshots":[]}"""),
        (111, 4, 11, 2.32, 0, "wf_xtra_mov_carry_users", """{"Version":1,"IntParams":[0,900],"Text":"","SelectedItems":[],"Delay":0,"SelectionCode":0,"ScoreQuotaPerGame":null,"TemporaryPlacement":null,"SecondarySelectedItems":[],"FurniSources":{},"UserSources":{},"VariableIds":[],"Snapshots":[]}"""),
        (112, 4, 11, 2.69, 0, "wf_xtra_anim_time", """{"Version":1,"IntParams":[200],"Text":"","SelectedItems":[],"Delay":0,"SelectionCode":0,"ScoreQuotaPerGame":null,"TemporaryPlacement":null,"SecondarySelectedItems":[],"FurniSources":{},"UserSources":{},"VariableIds":[],"Snapshots":[]}"""),
        (106, 5, 11, 0.00, 0, "wf_trg_recv_signal", """{"Version":1,"IntParams":[0,100],"Text":"","SelectedItems":[113],"Delay":0,"SelectionCode":0,"ScoreQuotaPerGame":null,"TemporaryPlacement":null,"SecondarySelectedItems":[],"FurniSources":{"items":100},"UserSources":{},"VariableIds":[],"Snapshots":[]}"""),
        (109, 5, 11, 0.65, 2, "wf_act_match_to_sshot", """{"Version":1,"IntParams":[0,0,1,1,0],"Text":"","SelectedItems":[119,118,117,116,115,114],"Delay":0,"SelectionCode":0,"ScoreQuotaPerGame":null,"TemporaryPlacement":null,"SecondarySelectedItems":[],"FurniSources":{"movers":0},"UserSources":{},"VariableIds":[],"Snapshots":[{"ItemId":119,"DefinitionId":29284,"X":9,"Y":13,"Z":0,"Rotation":0,"State":""},{"ItemId":118,"DefinitionId":29284,"X":8,"Y":13,"Z":0,"Rotation":0,"State":""},{"ItemId":117,"DefinitionId":29284,"X":7,"Y":13,"Z":0,"Rotation":0,"State":""},{"ItemId":116,"DefinitionId":29284,"X":6,"Y":13,"Z":0,"Rotation":0,"State":""},{"ItemId":115,"DefinitionId":29284,"X":4,"Y":13,"Z":0,"Rotation":0,"State":""},{"ItemId":114,"DefinitionId":29284,"X":5,"Y":13,"Z":0,"Rotation":0,"State":""}]}"""),
        (121, 5, 11, 1.30, 0, "wf_xtra_mov_physics", """{"Version":1,"IntParams":[0,1,1,0,0,0,0],"Text":"","SelectedItems":[],"Delay":0,"SelectionCode":0,"ScoreQuotaPerGame":null,"TemporaryPlacement":null,"SecondarySelectedItems":[],"FurniSources":{},"UserSources":{},"VariableIds":[],"Snapshots":[]}"""),
        (113, 5, 11, 1.67, 2, "wf_antenna2", null),
        (115, 4, 13, 0, 0, "wf_colortile", null),
        (114, 5, 13, 0, 0, "wf_colortile", null),
        (116, 6, 13, 0, 0, "wf_colortile", null),
        (117, 7, 13, 0, 0, "wf_colortile", null),
        (118, 8, 13, 0, 0, "wf_colortile", null),
        (119, 9, 13, 0, 0, "wf_colortile", null),
        (120, 10, 13, 0, 0, "wf_colortile", null)
    ];
    private static readonly uint[] Line = [115, 114, 116, 117, 118, 119];

    // model_bc_14 as stored live (custom model, door (3, 5)); the conveyor runs along row 13.
    private static readonly string Model14 = """
        xxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
        xxxx00000000xxxxxxxxxxxxxxxxxx
        xxxx00000000xxxxxxxxxxxxxxxxxx
        xxxx00000000xxxxxxxxxxxxxxxxxx
        xxx000000000xxxxxxxxxxxxxxxxxx
        xxx000000000xxxxxxxxxxxxxxxxxx
        xxx000000000xxxxxxxxxxxxxxxxxx
        xxx000000000xxxxxxxxxxxxxxxxxx
        xxx00000000000xxxxxxxxxxxxxxxx
        xxx000000000000000xxxxxxxxxxxx
        xxx000000000000xx00xxxxxxxxxxx
        xxx000000000x00xxx00xxxxxxxxxx
        xxx000000000000xxxx0xxxxxxxxxx
        xxx0000000000000xxx00xxxxxxxxx
        xxx0000000x00xx00xx00xxxxxxxxx
        xxx000000000xxxx00x00xxxxxxxxx
        xxx0000xxxxxxxxxx0xxxxxxxxxxxx
        xxxxxx00xxxxxxxx00xxxxxxxxxxxx
        xxxxxx000xxxxxxx00xxxxxxxxxxxx
        xxxxxx0000xxxxxx00xxxxxxxxxxxx
        xxxxxxx0x00xxxxx0xxxxxxxxxxxxx
        xxxxxxx00x00xxxx00xxxxxxxxxxxx
        xxxxxxxx0xx00xxx0xxxxxxxxxxxxx
        xxxxxxxx00xx00x00xxxxxxxxxxxxx
        xxxxxxxxx0xxxx00xxxxxxxxxxxxxx
        xxxxxxxxx0xxxxxxxxxxxxxxxxxxxx
        xxxxxxxxx0xxxxxxxxxxxxxxxxxxxx
        xxxxxxxxx0xxxxxxxxxxxxxxxxxxxx
        xxxxxxxxx0xxxxxxxxxxxxxxxxxxxx
        xxxxxxxxx0xxxxxxxx0000xxxxxxxx
        xxxxxxxxx0xxxxxxx00xx00xxxxxxx
        xxxxxxxxx0xxxxxx00xxxx00xxxxxx
        xxxxxxxxx0xxxxx00xxxxxx00xxxxx
        xxxxxxxx00xxxx00xxxxxxxx0xxxxx
        xxxxxxxx0xxxx00xxxxxxxxx0xxxxx
        xxxxxxxx00xx00xxxxxxxxxx0xxxxx
        xxxxxxxx0xx00xxxxxxxxxxx0xxxxx
        xxxxxxxx0x00xxxxxxxxxxx00xxxxx
        xxxxxxxx000xxxxxxxxxxxx0xxxxxx
        xxxxxxxx00xxxxxxxxxxxxx0xxxxxx
        xxxxxxxxxxxxxxxxxxxxxx00xxxxxx
        xxxxxxxxxxxxxxxxxxxxx00xxxxxxx
        xxxxxxxxxxxxxxxxxxxxx0xxxxxxxx
        xxxxxxxxxxxxxxxxxxxx00xxxxxxxx
        x00xx000000xxxxxxxxx0xxxxxxxx0
        xxx000xxxx000xxxxxx00xxxxxxx00
        xxx00xxxxxxx0xxxxxx0xxxxxx000x
        x000xxxxxxxx00xxxxx0xxxxx00xxx
        xx0xxxxxxxxxx0xxxx00xxxxxxxxxx
        xx00xxxxxxxxx00xxx0xxxxxxxxxxx
        xxx00xxxxxxxxx0xx00xxxxxxxxxxx
        xxxx00xxxxxxxx00x0xxxxxxxxxxxx
        xxxxx00xxxxxxxx000xxxxxxxxxxxx
        xxxxxx00xxxxxxxx0xxxxxxxxxxxxx
        xxxxxxx0xxxxxxxxxxxxxxxxxxxxxx
        0xxxxxx00xxxxxxxxxxxxxxxxxxxxx
        x00000000xxxxxxxxxxxxxxxxxxxxx
        xx0xxxx00000xxxxxxxxxxxxxxxxxx
        xxxxxxxx0xx000xxxxxxxxxxxxxxxx
        xxxxxxxx00xxx00xxxxxxxxxxxxxxx
        xxxxxxxxx0xxxx00xxxxxxxxxxxxxx
        xxxxxxxxxx00x00xxxxxxxxxxxxxxx
        xxxxxxxxxxx000xxxxxxxxxxxxxxxx
        """.ReplaceLineEndings("\r");

    // Room 14 as re-read live after the glide edits. Change Furni Direction 107 now takes its furni from the signal source.
    private static readonly (uint Id, int X, int Y, double Z, int Rot, string Name, string? Json)[] LiveRoom14 =
        Configure(Room14, (110, [1, 1, 0, 0, 900, 0, 0]), (109, [0, 0, 1, 1, 100]), (121, [0, 0, 1, 0, 0, 0, 900]))
            .Select(entry => entry.Id == 107 ? entry with { Json = Source(entry.Json!, 201) } : entry).ToArray();

    // As saved live, the front tile steps into unpicked tile 120 and every step mover is blocked by furniture unless the
    // physics add-on moves it through, so the whole line waits. The snapshot restore's source 0 is the antenna, which has no snapshot.
    [Fact]
    public void LiveRoom14LineWaitsBehindItsUnpickedEndTile()
    {
        var f = new Fixture(Room14);
        var rider = f.User(4, 13);

        f.Advance(1000);

        Assert.Equal((4, 13), (rider.X, rider.Y));
        AssertLineAtSnapshot(f);
    }

    // The same room once stack A moves through all furni, the restore uses its picked furni and stack B moves through all users:
    // each repeater pulse slides the line one tile east carrying the rider, and the signal puts the tiles back under them.
    [Fact]
    public void Room14GlidesTheRiderAcrossTheLineWithRoomWideSources()
    {
        var f = new Fixture(Configure(Room14, (110, [1, 1, 0, 0, 900, 0, 0]), (109, [0, 0, 1, 1, 100]), (121, [0, 0, 1, 0, 0, 0, 900])));
        var rider = f.User(4, 13);

        f.Advance(200);

        for (var pulse = 1; pulse <= 7; pulse++) {
            f.Advance(200);

            // The rider leaves the line onto unpicked tile 120 and stays there.
            Assert.Equal((Math.Min(4 + pulse, 10), 13, 0.1), (rider.X, rider.Y, rider.Z));
            AssertLineAtSnapshot(f);
        }
    }

    // Live: a repeater stack has no signal, so a signal-sourced mover selects nothing and the line never moves.
    [Fact]
    public void LiveRoom14SignalSourcedMoverSelectsNothingFromItsRepeater()
    {
        var f = new Fixture(LiveRoom14, live: true);
        var rider = f.Walker(1, 4, 13);

        for (var pulse = 1; pulse <= 6; pulse++) {
            f.Advance(100);

            Assert.Equal((4, 13, 0.1), (rider.X, rider.Y, rider.Z));
            AssertLineAtSnapshot(f);
        }
    }

    // The live room on its real heightmap with v2 movement: once 107 takes its picked furni again, each repeater pulse carries
    // a rider who walked onto the first tile one tile east; standing on the moving tile never blocks it.
    [Fact]
    public void LiveRoom14GlidesAWalkedOnRiderWithPickedFurni()
    {
        var f = new Fixture(Picked(LiveRoom14), live: true);
        var rider = f.Walker(1, 4, 13);
        Assert.Equal((4, 13, 0.1), (rider.X, rider.Y, rider.Z));
        f.Advance(100);

        for (var pulse = 1; pulse <= 6; pulse++) {
            f.Advance(100);
            Assert.Equal((4 + pulse, 13, 0.1), (rider.X, rider.Y, rider.Z));
            Assert.Equal(10, f.Items[119].GetX);

            f.Advance(100);
            AssertLineAtSnapshot(f);
        }
    }

    // A user standing on the end tile is not carried, so the front tile cannot step under them and the rider is not pushed onto them.
    [Fact]
    public void LiveRoom14UserOnTheEndTileBlocksTheFrontTile()
    {
        var f = new Fixture(Picked(LiveRoom14), live: true);
        var rider = f.Walker(1, 4, 13);
        var idle = f.Walker(2, 10, 13);

        f.Advance(1200);

        Assert.Equal((9, 13), (rider.X, rider.Y));
        Assert.Equal((10, 13), (idle.X, idle.Y));
        Assert.Equal(9, f.Items[119].GetX);
    }

    // Live: a user beside the line clicks the front tile while the conveyor runs. Its tile leaves and returns between room
    // ticks, so the target height flips between the tile top and the floor; the step must still land on the walkable tile.
    [Fact]
    public void LiveRoom14UserStepsOntoTheFrontTileWhileTheLineMoves()
    {
        var f = new Fixture(Picked(LiveRoom14), live: true);
        var user = f.Walker(1, 3, 12, 4, 12);
        user.MoveTo(4, 13);

        f.Advance(1000);

        Assert.Equal(13, user.Y);
        Assert.InRange(user.X, 4, 10);
    }

    // Once landed, the conveyor carries the user who stepped on to the unpicked end tile.
    [Fact]
    public void LiveRoom14UserWhoStepsOntoTheMovingLineGlidesToTheEndTile()
    {
        var f = new Fixture(Picked(LiveRoom14), live: true);
        var user = f.Walker(1, 3, 12, 4, 12);
        user.MoveTo(4, 13);

        f.Advance(4000);

        Assert.Equal((10, 13, 0.1), (user.X, user.Y, user.Z));
    }

    private static (uint Id, int X, int Y, double Z, int Rot, string Name, string? Json)[] Picked(
        (uint Id, int X, int Y, double Z, int Rot, string Name, string? Json)[] layout) =>
        layout.Select(entry => entry.Id == 107 ? entry with { Json = Source(entry.Json!, 100) } : entry).ToArray();

    private static string Source(string json, int source) => JsonSerializer.Serialize(JsonSerializer.Deserialize<WiredConfiguration>(json)! with
    {
        IntParams = [2, 0, source, 1],
        FurniSources = ImmutableDictionary<string, int>.Empty.Add("items", source)
    });

    private static void AssertLineAtSnapshot(Fixture f) =>
        Assert.Equal([4, 5, 6, 7, 8, 9], Line.Select(id => f.Items[id]).Select(item => item.GetX));

    private static (uint Id, int X, int Y, double Z, int Rot, string Name, string? Json)[] Configure(
        (uint Id, int X, int Y, double Z, int Rot, string Name, string? Json)[] layout, params (uint Id, int[] Ints)[] changes) =>
        layout.Select(entry => changes.Any(change => change.Id == entry.Id)
            ? entry with
            {
                Json = JsonSerializer.Serialize(JsonSerializer.Deserialize<WiredConfiguration>(entry.Json!)! with
                {
                    IntParams = [.. changes.First(change => change.Id == entry.Id).Ints]
                })
            }
            : entry).ToArray();

    private sealed class Fixture
    {
        private readonly Room _room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        private readonly Gamemap _map;
        private readonly WiredComponent _wired;
        private readonly ConcurrentDictionary<int, RoomUser> _users;
        private readonly bool _live;
        private long _now;
        public ConcurrentDictionary<uint, Item> Items { get; }

        public Fixture((uint Id, int X, int Y, double Z, int Rot, string Name, string? Json)[] layout, bool live = false)
        {
            _room.Id = 14;
            Set(_room, "_interactionClock", TimeProvider.System);
            _map = new(_room, live ? new RoomModel("model_bc_14", 3, 5, 0, 2, Model14, 0, 0, true)
                    : new RoomModel("model_bc_14", 0, 0, 0, 0, string.Join('\r', Enumerable.Repeat(new string('0', 16), 18)), 0, 0, true),
                TestLogging.Navigation, live ? new TestRoomSettings(new() { ["pathfinding.engine"] = "v2", ["pathfinding.layering_enabled"] = "1" }) : TestRoomSettings.Empty,
                TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
            var handler = new RoomItemHandling(_room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty,
                TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
            Set(_room, "_gamemap", _map);
            Set(_room, "_roomItemHandling", handler);
            var users = new RoomUserManager(_room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused,
                TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
            Set(_room, "_roomUserManager", users);
            TestRoomUserSnapshots.Install(_room);
            _live = live;

            if (!live) {
                typeof(Gamemap).GetProperty("GameMap")!.SetValue(_map, new byte[_map.Model.MapSizeX, _map.Model.MapSizeY]);
                typeof(Gamemap).GetProperty("EffectMap")!.SetValue(_map, new byte[_map.Model.MapSizeX, _map.Model.MapSizeY]);
            }

            Items = (ConcurrentDictionary<uint, Item>)Get(handler, "_floorItems");
            _users = (ConcurrentDictionary<int, RoomUser>)Get(users, "_users");
            var saved = layout.Where(entry => entry.Json != null)
                .ToDictionary(entry => entry.Id, entry => JsonSerializer.Deserialize<WiredConfiguration>(entry.Json!)!);
            _wired = new(_room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance,
                new SavedConfigurations(saved), TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance,
                TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
            Set(_room, "_wiredComponent", _wired);
            var engine = Get(_wired, "_engine");
            engine.GetType().GetField("_now", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, (Func<long>)(() => _now));

            foreach (var entry in layout) {
                var tile = entry.Name == "wf_colortile";
                var antenna = entry.Name == "wf_antenna2";
                var interaction = tile ? "pressure_pad" : antenna ? "antenna" : entry.Name;
                var item = new Item
                {
                    Id = entry.Id,
                    RoomId = 14,
                    ExtraData = new LegacyDataFormat { Data = "" },
                    Definition = new()
                    {
                        Id = tile ? 29284u : 0,
                        Type = ItemType.Floor,
                        Width = 1,
                        Length = 1,
                        Height = tile ? 0.1 : antenna ? 1 : 0.65,
                        Stackable = true,
                        Walkable = tile,
                        Modes = tile ? 7 : 1,
                        ItemName = entry.Name,
                        PublicName = entry.Name,
                        InteractionName = interaction,
                        InteractionType = InteractionTypes.GetTypeFromString(interaction),
                        AdjustableHeights = [],
                        VendingIds = []
                    }
                };
                Set(item, "_room", _room);
                item.SetState(entry.X, entry.Y, entry.Z, Gamemap.GetAffectedTiles(1, 1, entry.X, entry.Y, entry.Rot));
                item.Rotation = entry.Rot;
                Items[item.Id] = item;
                _map.AddToMap(item);

                if (live) {
                    item.EnableNavigationSynchronization();
                    _map.Navigation!.Inputs.Attach(item);
                }
            }

            if (live) {
                _map.GenerateMaps();
            }

            foreach (var item in Items.Values.Where(item => item.IsWired)) {
                Assert.NotNull(_wired.LoadWiredBox(item));
            }
        }

        public RoomUser User(int x, int y)
        {
            var user = new RoomUser(1, 14, 1, _room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { InternalRoomId = 1 };
            _users[user.VirtualId] = user;
            _map.AddUserToMap(user, new(x, y));
            user.SetPos(x, y, _map.SqAbsoluteHeight(x, y));
            _map.GameMap[x, y] = 1;

            return user;
        }

        // A user who walked in from (x, y - 1), as the v2 executor places them.
        public RoomUser Walker(int virtualId, int x, int y) => Walker(virtualId, x, y - 1, x, y);

        // A user who walked in from (fromX, fromY).
        public RoomUser Walker(int virtualId, int fromX, int fromY, int x, int y)
        {
            var client = new Client();
            client.SetHabbo(new Habbo
            {
                Id = virtualId,
                Username = $"user{virtualId}",
                CurrentRoom = _room,
                Access = UserAccess.Empty,
                Effects = new(new FixedTimeProvider(FixedTimeProvider.Epoch)),
                HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0)
            });
            var user = new RoomUser(virtualId, 14, virtualId, _room, client, TestChatEmotions.Unused, new TestRewardProgress())
            {
                InternalRoomId = virtualId,
                X = fromX,
                Y = fromY,
                Z = _map.SqAbsoluteHeight(fromX, fromY)
            };
            _users[user.VirtualId] = user;
            _room.RunFastPass(() => _map.Navigation!.Admit(user));
            user.MoveTo(x, y);

            for (var i = 0; i < 10 && ((user.X, user.Y) != (x, y) || user.IsWalking); i++) {
                Full();
            }

            return user;
        }

        private void Full() => _room.RunFastPass(() =>
        {
            _map.Navigation!.ApplyDirty();
            _map.Navigation.DrainCommands();
            _map.Gates.Drain();
            _room.GetRoomUserManager().OnCycle();
            _wired.OnCycle();
            _map.FlushPlacementUpdates();
        });

        // In 50 ms room passes.
        public void Advance(int milliseconds)
        {
            for (var end = _now + milliseconds; _now < end;) {
                _now += 50;

                if (!_live) {
                    _wired.OnCycle();
                    _wired.OnFastCycle();
                }
                else if (_now % 500 == 0) {
                    Full();
                }
                else {
                    _room.RunFastPass(_wired.OnFastCycle);
                }
            }
        }

        private static void Set(object target, string name, object value) => Field(target.GetType(), name).SetValue(target, value);
        private static object Get(object target, string name) => Field(target.GetType(), name).GetValue(target)!;
        private static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? Field(type.BaseType ?? throw new MissingFieldException(name), name);
    }

    private sealed class Client() : GameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
    {
        internal override (bool Complete, bool Malformed, uint MessageId, int HeaderLength, int Length) GetMessageIdAndPacketLength(ReadOnlyMemory<byte> buffer) =>
            (true, false, 0, 0, 0);
        public override void CreateHeader(Memory<byte> memory, uint messageId) { }
    }

    private sealed class SavedConfigurations(IReadOnlyDictionary<uint, WiredConfiguration> saved) : IWiredConfigurationStore
    {
        public WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor) => saved.GetValueOrDefault(itemId);
        public void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration) { }
        public void Reset(IReadOnlyCollection<uint> itemIds) { }
    }
}
