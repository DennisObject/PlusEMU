using System.Collections.Concurrent;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Database;
using Plus.Database.Interfaces;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Rooms.Games.Teams;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Plus.Communication.Flash;
using Plus.Communication.Revisions;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Modern.Conditions;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;
using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Badges;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Settings;
using Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;

namespace Plus.Tests;

[CollectionDefinition("Modern Wired database seam", DisableParallelization = true)]
public sealed class ModernWiredDatabaseCollection;

[Collection("Modern Wired database seam")]
public class ModernWiredRuntimeTests
{
    private static WiredModernAction ActionBox(Room room, string name, WiredCounterController? clocks = null, WiredRoomLog? log = null) =>
        new(room, MakeItem(100, name), Descriptor(name), clocks ?? new(), _ => { }, (_, _, _) => { }, log ?? new(), TestLogging.Logger);

    [Fact]
    public void AllImplementedEditorsHaveValidatedDefaults()
    {
        var (room, _, _) = World();
        foreach (var name in WiredTriggerConfiguration.Events.Keys)
            Assert.True(WiredTriggerConfiguration.TryValidate(name, WiredTriggerConfiguration.Defaults(name), out _, out _), name);
        foreach (var name in WiredConditionConfiguration.PositiveNames.Concat(WiredConditionConfiguration.NegativeNames.Keys))
            Assert.True(WiredConditionConfiguration.TryValidate(name, WiredConditionConfiguration.Defaults(name), out _, out _), name);
        foreach (var name in WiredMovementActions.Names.Concat(WiredModernAction.OtherNames).Concat(WiredBotActions.Names))
            Assert.True(ActionBox(room, name).TryValidateConfiguration(WiredActionConfiguration.Defaults(name), out _, out _), name);
    }

    [Fact]
    public void NegativeStackAndSplitSignalsCallRealOperationsWithSeparateRoles()
    {
        var (room, _, _) = World();
        var antenna = MakeItem(1, "antenna"); var forwarded = MakeItem(2, "forwarded");
        var clicked = new RoomUser(1, 0, 7, room);
        var operations = new RecordingOperations();
        var context = new WiredRuntimeContext(room, new(WiredEventKind.ClickUser) { TargetUser = clicked },
            new(() => new[] { antenna, forwarded }, () => new[] { clicked }), operations);
        var call = ActionBox(room, "wf_act_neg_call_stacks");
        call.Item.SetState(2, 2, 0, Gamemap.GetAffectedTiles(1, 1, 2, 2, 0));
        Assert.True(call.TryValidateConfiguration(new() { IntParams = [100], SelectedItems = [1] }, out var callConfig, out _));
        call.ApplyConfiguration(callConfig);
        Assert.True(call.IsNegative); Assert.True(call.Execute(context));
        Assert.True(operations.CallNegative); Assert.Equal(new uint[] { 1 }, operations.Called);
        call.Item.SetState(0, 0, 0, Gamemap.GetAffectedTiles(1, 1, 0, 0, 0));
        Assert.False(call.Execute(context)); Assert.Empty(operations.Called);
        var signal = ActionBox(room, "wf_act_neg_send_signal");
        Assert.True(signal.TryValidateConfiguration(new() { IntParams = [1, 100, 11, 1, 1, 0], SelectedItems = [1], Text = "2" }, out var signalConfig, out _));
        signal.ApplyConfiguration(signalConfig); Assert.True(signal.Execute(context));
        var received = Assert.Single(operations.Signals);
        Assert.True(received.Negative); Assert.Equal(new uint[] { 1 }, received.Receivers);
        Assert.Equal(new uint[] { 2 }, received.Selection.FurniIds); Assert.Equal(new[] { 7 }, received.Selection.UserIds);
    }

    [Fact]
    public void ConfiguredClockActionControlsActualAttachedClock()
    {
        var (room, _, _) = World(); var item = MakeItem(1, "wf_upcounter1");
        var clocks = new WiredCounterController(); clocks.Attach(item);
        var box = ActionBox(room, "wf_act_adjust_clock", clocks);
        box.TryValidateConfiguration(new() { IntParams = [2, 100, 1, 3], SelectedItems = [1] }, out var config, out _);
        box.ApplyConfiguration(config); Assert.True(box.Execute(Context(room, new(WiredEventKind.Use), [item], [])));
        Assert.Equal(61500, clocks.ReadMilliseconds(item)); Assert.False(clocks.HasRunning);
        clocks.Control(item, 0, 0); Assert.True(clocks.HasRunning); clocks.Forget(item); Assert.False(clocks.HasRunning);
    }

    [Fact]
    public void LogActionWritesBoundedRoomMonitorAndEmptyTextHasNoEffect()
    {
        var (room, _, _) = World(); var log = new WiredRoomLog(2);
        var box = ActionBox(room, "wf_act_neg_log", log: log);
        box.TryValidateConfiguration(new() { IntParams = [1, 0], Text = "First" }, out var config, out _);
        box.ApplyConfiguration(config); Assert.True(box.Execute(Context(room, new(WiredEventKind.Use), [], [])));
        Assert.Equal("First", Assert.Single(log.Read(0, 10).Entries).Message);
        log.Append(2, 100, "Second", DateTimeOffset.UtcNow); log.Append(1, 100, "Third", DateTimeOffset.UtcNow);
        Assert.Equal(2, log.Read(0, 10).Total); Assert.Equal("Third", Assert.Single(log.Read(0, 10, 1, "third").Entries).Message);
        box.ApplyConfiguration(config with { Text = "" }); Assert.False(box.Execute(Context(room, new(WiredEventKind.Use), [], [])));
    }

    [Fact]
    public async Task FiredLogLineReachesTheLogPageAndMonitorForInspectorsOnly()
    {
        using var f = new TeleportFixture();
        f.Room.OwnerName = "Alice"; f.Room.Type = "private"; f.Room.UsersWithRights = [];
        var store = new MonitorSettingsStore();
        var settings = new WiredRoomSettings(f.Room, store);
        ((ConditionalWeakTable<Room, WiredRoomSettings>)typeof(WiredRoomSettings).GetField("Instances", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!).Add(f.Room, settings);
        var item = MakeItem(102, "wf_act_log");
        var box = Assert.IsType<WiredModernAction>(f.Room.GetWired().CreateConfiguredBox(item, Descriptor("wf_act_log")));
        Assert.True(box.TryValidateConfiguration(new() { IntParams = [2, 0], Text = "Gate opened" }, out var config, out _));
        box.ApplyConfiguration(config);
        f.Items[102] = item; f.Engine.Add(box);
        f.Fire();

        var alice = Capture(f.Habbo.Client);
        await new WiredRoomLogsPageEvent().Parse(f.Room, f.Habbo.Client, Request(1, 50, -1, -1, ""));
        var page = Reply(alice, 918);
        Assert.Equal((1, 1, 50, 1), (page.Int(), page.Int(), page.Int(), page.Int()));
        Assert.Equal((1d, 2, 8, "Gate opened"), (page.Long(), page.Byte(), page.Byte(), page.String()));
        var millis = page.Long();
        Assert.InRange(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - millis, 0, 60_000);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds((long)millis).UtcDateTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture), page.String());
        Assert.Equal((false, false, false), (page.Bool(), page.Bool(), page.Bool()));
        page.End();

        await new WiredRoomLogsPageEvent().Parse(f.Room, f.Habbo.Client, Request(9, 50, 2, 8, " GATE "));
        page = Reply(alice, 918);
        Assert.Equal((1, 1, 50, 1), (page.Int(), page.Int(), page.Int(), page.Int()));
        Assert.Equal("Gate opened", page.Skip(2, 1, 1).String()); page.Skip(2).String();
        Assert.Equal((true, 2, true, 8, true, "GATE"), (page.Bool(), page.Byte(), page.Bool(), page.Byte(), page.Bool(), page.String()));
        page.End();
        await new WiredRoomLogsPageEvent().Parse(f.Room, f.Habbo.Client, Request(1, 50, -1, 4, "")); // a source Plus never writes
        page = Reply(alice, 918);
        Assert.Equal((0, 1, 50, 0), (page.Int(), page.Int(), page.Int(), page.Int()));

        await new WiredMonitorRequestEvent().Parse(f.Room, f.Habbo.Client, Request(0));
        var monitor = Reply(alice, 5101);
        monitor.Skip(1); Assert.Equal(10000, monitor.Int()); Assert.False(monitor.Bool());
        monitor.Skip(1); Assert.Equal(100, monitor.Int()); monitor.Skip(3); Assert.Equal(32, monitor.Int());
        Assert.Equal((0, 1000, 0, 0, 0, 0, 0, 0), (monitor.Int(), monitor.Int(), monitor.Int(), monitor.Int(), monitor.Int(), monitor.Int(), monitor.Int(), monitor.Int()));
        Assert.Equal(4, monitor.Int());
        var tallies = Enumerable.Range(0, 4).Select(_ => (Type: monitor.String(), Severity: monitor.String(), Count: monitor.Int(), Seconds: monitor.Int(),
            Reason: monitor.String(), Label: monitor.String(), Id: monitor.Int())).ToArray();
        Assert.Equal(["EXECUTION_CAP", "DELAYED_EVENTS_CAP", "RECURSION_TIMEOUT", "WIRED_LOG"], tallies.Select(x => x.Type));
        Assert.All(tallies[..3], tally => Assert.Equal(("ERROR", 0, 0), (tally.Severity, tally.Count, tally.Seconds)));
        Assert.Equal(("WARNING", 1, "Gate opened", "wf_act_log", 102), (tallies[3].Severity, tallies[3].Count, tallies[3].Reason, tallies[3].Label, tallies[3].Id));
        Assert.Equal(1, monitor.Int());
        Assert.Equal(("WIRED_LOG", "WARNING"), (monitor.String(), monitor.String()));
        Assert.Equal(tallies[3].Seconds, monitor.Int());
        Assert.Equal(("Gate opened", "wf_act_log", 102), (monitor.String(), monitor.String(), monitor.Int()));
        monitor.End();

        // Without inspect rights neither request answers; with inspect only, a clear is refused.
        var bob = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient) { Revision = f.Habbo.Client.Revision };
        bob.SetHabbo(new Habbo { Id = 2, Username = "Bob", CurrentRoom = f.Room });
        var bobReplies = Capture(bob);
        await new WiredRoomLogsPageEvent().Parse(f.Room, bob, Request(1, 50, -1, -1, ""));
        await new WiredMonitorRequestEvent().Parse(f.Room, bob, Request(0));
        Assert.Empty(bobReplies);
        store.Saved = new(InspectMask: (int)WiredRoomAccess.Everyone); settings.Reload();
        await new WiredMonitorRequestEvent().Parse(f.Room, bob, Request(1));
        Assert.Empty(bobReplies);
        var pages = new WiredRoomLogsPageEvent();
        await pages.Parse(f.Room, bob, Request(1, 50, -1, -1, ""));
        await pages.Parse(f.Room, bob, Request(1, 50, -1, -1, "")); // inside the 250 ms page interval
        Assert.Equal(1, Reply(bobReplies, 918).Int());
        Assert.Empty(bobReplies);

        await new WiredMonitorRequestEvent().Parse(f.Room, f.Habbo.Client, Request(1));
        monitor = Reply(alice, 5101).Skip(16, 1);
        Assert.Equal(4, monitor.Int());
        for (var i = 0; i < 4; i++) { monitor.String(); monitor.String(); Assert.Equal(0, monitor.Int()); monitor.Skip(1).String(); monitor.String(); monitor.Int(); }
        Assert.Equal(0, monitor.Int());
        monitor.End();
        await new WiredRoomLogsPageEvent().Parse(f.Room, f.Habbo.Client, Request(1, 50, -1, -1, "x", 1)); // trailing data is malformed
        Assert.Empty(alice);
    }

    private static List<(uint Header, byte[] Body)> Capture(GameClient client)
    {
        var replies = new List<(uint, byte[])>();
        client.SendCallback = args => { replies.Add(((uint)FlashGameClient.DecodeInt16(args.MemoryBuffer.Slice(4, 2)), args.MemoryBuffer[6..].ToArray())); return true; };
        return replies;
    }

    private static WireReader Reply(List<(uint Header, byte[] Body)> replies, uint header)
    {
        var reply = replies[0]; replies.RemoveAt(0);
        Assert.Equal(header, reply.Header);
        return new(reply.Body);
    }

    private static FlashIncomingPacket Request(params object[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            if (value is string text)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(text); var length = new byte[2];
                System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(length, (short)bytes.Length);
                stream.Write(length); stream.Write(bytes);
            }
            else
            {
                var bytes = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, (int)value); stream.Write(bytes);
            }
        }
        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    /// <summary>Reads a reply the way the client's parsers do, down to its last byte.</summary>
    private sealed class WireReader(byte[] body)
    {
        private int _position;
        public int Int() { var value = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(_position)); _position += 4; return value; }
        public int Byte() => body[_position++];
        public bool Bool() { var value = Byte(); Assert.InRange(value, 0, 1); return value == 1; }
        public string String()
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(body.AsSpan(_position)); _position += 2;
            var text = System.Text.Encoding.UTF8.GetString(body, _position, length); _position += length; return text;
        }
        // The client's readWiredLong: an unsigned high and low half.
        public double Long() => (uint)Int() * 4294967296d + (uint)Int();
        public WireReader Skip(int ints, int bytes = 0, int moreBytes = 0) { _position += ints * 4 + bytes + moreBytes; return this; }
        public void End() => Assert.Equal(body.Length, _position);
    }

    private sealed class MonitorSettingsStore : IWiredRoomSettingsStore
    {
        public WiredRoomSettingsSnapshot? Saved = new();
        public WiredRoomSettingsSnapshot? Load(uint roomId) => Saved;
        public void Save(uint roomId, int actorId, bool staff, WiredRoomSettingsSnapshot? expected, WiredRoomSettingsSnapshot settings) =>
            throw new NotSupportedException();
    }

    [Fact]
    public void LegacyEditorConversionPreservesSavedSnapshotAndPlaceholderText()
    {
        var (room, _, _) = World(); var picked = MakeItem(1, "test");
        var old = new MatchPositionBox(room, MakeItem(100, "wf_act_match_to_sshot"))
            { StringData = "1;1;1", ItemsData = "1:2,1,3.25,4,old,raw,state" };
        old.SetItems[1] = picked; picked.LegacyDataString = "changed";
        Assert.True(WiredLegacyConfigurationAdapter.TryConvert(old, Descriptor("wf_act_match_to_sshot"), out var config));
        Assert.Equal("old,raw,state", Assert.Single(config.Snapshots).State); Assert.Equal(3.25, config.Snapshots[0].Z);
        var chat = new ShowMessageBox(room, MakeItem(101, "wf_act_show_message")) { StringData = "Hello %USERNAME%" };
        Assert.True(WiredLegacyConfigurationAdapter.TryConvert(chat, Descriptor("wf_act_show_message"), out var converted));
        Assert.Equal(new[] { 0, 0, 34, -1 }, converted.IntParams); Assert.Equal(chat.StringData, converted.Text);
    }

    [Fact]
    public void ActiveChatAndMovementComposersPreserveParserFields()
    {
        var fields = new List<object>(); var packet = DispatchProxy.Create<IOutgoingPacket, RecordingProxy>();
        ((RecordingProxy)(object)packet).InvokeMethod = (_, args) => { fields.Add(args![0]!); return null; };
        new WiredChatComposer(7, "Hello", 252, 2, true).Compose(packet);
        Assert.Equal(new object[] { 7, "Hello", 0, 252, 0, 5, 2 }, fields);
        fields.Clear(); new WiredMovementComposer(1, 10, 0, 1, 1.25, 2, 2, 3.5, 4, 4, 750).Compose(packet);
        Assert.Equal(new object[] { 1, 1, 0, 1, 2, 2, "1.25", "3.5", 10, 4, 750, 0, 0, 0 }, fields);
        fields.Clear(); new WiredRewardResultComposer(5).Compose(packet);
        Assert.Equal(new object[] { 5 }, fields); Assert.Equal(ServerPacketHeader.WiredRewardResultComposer, new WiredRewardResultComposer(5).MessageId);
        var (room, _, _) = World(); var box = ActionBox(room, "wf_act_show_message");
        Assert.True(box.TryValidateConfiguration(new() { IntParams = [0, 0, 252, 2], Text = "Hello" }, out var config, out _));
        Assert.Equal(2, config.IntParams[3]);
        Assert.False(box.TryValidateConfiguration(config with { IntParams = [0, 0, 252, 3] }, out _, out _));
    }

    private sealed class RecordingOperations : IWiredRuntimeOperations
    {
        public uint[] Called = []; public bool CallNegative;
        public List<(uint[] Receivers, WiredSelection Selection, bool Negative)> Signals = [];
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false)
        { Called = targets.Select(x => x.Id).ToArray(); CallNegative = negative; return Called.Length > 0; }
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false)
        { Signals.Add((receivers.Select(x => x.Id).ToArray(), selection.Copy(), negative)); return true; }
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }

    [Fact]
    public void TemporaryEffectLeasesPreserveNewEffectsOverlapsAndRoomVisitIdentity()
    {
        var (room, _, _) = World(); var user = new RoomUser(1, 0, 7, room);
        var effects = new WiredTemporaryEffects(); var current = 8; var attached = true;
        var first = effects.Acquire(user, () => current, value => current = value, () => attached);
        var second = effects.Acquire(user, () => current, value => current = value, () => attached);
        first(); Assert.Equal(4, current); second(); Assert.Equal(8, current); second(); Assert.Equal(8, current);
        var changed = effects.Acquire(user, () => current, value => current = value, () => attached);
        current = 12; changed(); Assert.Equal(12, current);
        var departed = effects.Acquire(user, () => current, value => current = value, () => attached);
        attached = false; current = -1; departed(); Assert.Equal(-1, current);
        attached = true; var capFailure = effects.Acquire(user, () => current, value => current = value, () => attached);
        capFailure(); Assert.Equal(-1, current); // Immediate cleanup when engine refuses the restore callback.
    }

    [Fact]
    public void TemporaryEffectsLifecycleReleasesOnlyAttachedUnchangedVisits()
    {
        var (room, _, _) = World(); var user = new RoomUser(1, 0, 7, room);
        var effects = new WiredTemporaryEffects(); var current = 8; var attached = true;
        var restore = effects.Acquire(user, () => current, value => current = value, () => attached);
        effects.Forget(user); Assert.Equal(8, current); current = 12; restore(); Assert.Equal(12, current);
        effects.Acquire(user, () => current, value => current = value, () => attached);
        effects.Clear(); Assert.Equal(12, current);
        effects.Acquire(user, () => current, value => current = value, () => attached);
        attached = false; current = -1; effects.Clear(); Assert.Equal(-1, current);
    }

    [Fact]
    public void HeadingRemembersTurnAndStopDoesNotInventMovement()
    {
        var item = MakeItem(1, "test"); var directions = new WiredDirectionalActions(); var attempts = new List<Point>();
        Assert.True(directions.MoveHeading(item, 0, 1, false, (x, y) => { attempts.Add(new(x, y)); return x == 1; }, (_, _) => [], (_, _) => throw new Exception()));
        Assert.Equal(new[] { new Point(0, -1), new Point(1, 0) }, attempts);
        attempts.Clear(); Assert.True(directions.MoveHeading(item, 0, 1, false, (x, y) => { attempts.Add(new(x, y)); return true; }, (_, _) => [], (_, _) => throw new Exception()));
        Assert.Equal(new Point(1, 0), Assert.Single(attempts));
        attempts.Clear(); Assert.False(new WiredDirectionalActions().MoveHeading(item, 0, 6, false,
            (x, y) => { attempts.Add(new(x, y)); return false; }, (_, _) => [], (_, _) => throw new Exception()));
        Assert.Single(attempts);
        Assert.Equal(2, WiredDirectionalActions.AvatarRotation(0, 8)); Assert.Equal(6, WiredDirectionalActions.AvatarRotation(0, 9));
    }

    [Fact]
    public void ChaseQueriesNearestWithinThreeAndOrdersLongAxisFirst()
    {
        var (room, _, _) = World(); var item = MakeItem(1, "test");
        var near = new RoomUser(1, 0, 7, room) { X = 2, Y = 1 }; var far = new RoomUser(2, 0, 8, room) { X = 4, Y = 0 };
        Assert.Same(near, WiredDirectionalActions.Nearest(item, [far, near])); Assert.Null(WiredDirectionalActions.Nearest(item, [far]));
        Assert.Equal(new[] { new Point(1, 0), new Point(0, 1) }, WiredDirectionalActions.Steps(item, near, false));
        Assert.Equal(new[] { new Point(-1, 0), new Point(0, -1) }, WiredDirectionalActions.Steps(item, near, true));
    }

    [Fact]
    public void WiredFreezePreservesExistingGameFreezeAndConsumesTeleportCancelFlag()
    {
        var (room, _, _) = World(); var user = new RoomUser(1, 0, 7, room); var state = new WiredAvatarState();
        user.SetStatus("mv", "1,1,0"); user.IsWalking = true;
        Assert.True(state.FreezeUser(user, 0, false)); Assert.True(user.Frozen); Assert.False(user.CanWalk); Assert.False(user.IsWalking); Assert.False(user.HasStatus("mv"));
        Assert.False(state.Thaw(user, teleport: true)); Assert.True(user.Frozen);
        state.FreezeUser(user, 0, true); Assert.True(state.Thaw(user, teleport: true)); Assert.False(user.Frozen); Assert.True(user.CanWalk);
        user.Frozen = true; user.CanWalk = false; state.FreezeUser(user, 0, true); state.Thaw(user);
        Assert.True(user.Frozen); Assert.False(user.CanWalk); // A game freeze is independently owned.
    }

    [Theory]
    [InlineData(4, 0, 1)] [InlineData(6, 0, -1)] [InlineData(8, 1, -1)] [InlineData(11, -1, -1)]
    public void FurnitureMoveUsesActualPolarisDirectionNumbers(int raw, int dx, int dy)
    {
        var item = MakeItem(1, "test"); var moves = new List<Point>();
        Assert.True(new WiredMovementActions().Execute("wf_act_move_rotate", new() { IntParams = [raw, 0, 100] }, [item], [], [],
            (_, x, y, _, _) => { moves.Add(new(x, y)); return true; }, (_, _, _, _, _) => false, (_, _) => throw new Exception()));
        Assert.Equal(new Point(dx, dy), Assert.Single(moves));
    }

    [Theory]
    [InlineData(0, 0, -1)] [InlineData(1, 1, -1)] [InlineData(2, 1, 0)] [InlineData(3, 1, 1)]
    [InlineData(4, 0, 1)] [InlineData(5, -1, 1)] [InlineData(6, -1, 0)] [InlineData(7, -1, -1)]
    public void CurrentFourFieldMoveEditorUsesActualDirectionGrid(int direction, int dx, int dy)
    {
        var item = MakeItem(1, "test"); var moved = Point.Empty;
        Assert.True(new WiredMovementActions().Execute("wf_act_move_rotate", new() { IntParams = [direction, 0, 100, 0] }, [item], [], [],
            (_, x, y, _, _) => { moved = new(x, y); return true; }, (_, _, _, _, _) => throw new Exception(), (_, _) => throw new Exception()));
        Assert.Equal(new Point(dx, dy), moved);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(2, 2)] [InlineData(3, 7)] [InlineData(4, 6)] [InlineData(5, 4)]
    public void CurrentFourFieldMoveEditorUsesActualTurnLabels(int option, int expected)
    {
        var item = MakeItem(1, "test"); var rotation = -1;
        Assert.True(new WiredMovementActions().Execute("wf_act_move_rotate", new() { IntParams = [-1, option, 100, 1] }, [item], [], [],
            (_, _, _, _, _) => throw new Exception("Flag must select occupied-user blocking path"), (_, _, _, _, _) => false, (_, _) => { },
            (_, _, _, value, _) => { rotation = value; return true; }));
        Assert.Equal(expected, rotation);
    }

    [Fact]
    public void CurrentMoveCollisionFlagOverridesScopedThroughUsersInActualRoom()
    {
        var (room, map, items) = World(); var mover = MakeItem(1, "test"); items[1] = mover; map.AddToMap(mover);
        var occupant = new RoomUser(1, 0, 7, room) { X = 1, Y = 1 }; map.AddUserToMap(occupant, new(1, 1));
        var context = Context(room, new(WiredEventKind.Enter), [mover], [occupant]);
        context.Policy.Addons.Physics = new(false, new HashSet<uint>(), new HashSet<int> { 7 }, new HashSet<uint>());
        Assert.False(new WiredRoomMovement((_, _, _) => { }).MoveFurniture(context, mover, 1, 1, 0, null, blockOnUserCollision: true));
        Assert.Equal(Point.Empty, mover.Coordinate);
    }

    [Fact]
    public void RoomForwardingResolvesActualLinkSectionsPairsAndFallbackInOrder()
    {
        var link = MakeItem(1, "link"); link.ExtraData = new MapDataFormat(new() { ["internalLink"] = "23" });
        Assert.Equal(new WiredRoomForwarding.Destination(23), WiredRoomForwarding.Resolve([link], "99", _ => throw new Exception(), _ => throw new Exception(), TestLogging.Logger));
        link.ExtraData = new LegacyDataFormat { Data = "{\"room_linker\":{\"ItemId\":17}}" };
        Assert.Equal(new WiredRoomForwarding.Destination(42, 17), WiredRoomForwarding.Resolve([link], "99", id => id == 17 ? 42u : 0, _ => throw new Exception(), TestLogging.Logger));
        var tele = MakeItem(2, "tele"); tele.Definition.InteractionType = InteractionType.Teleport;
        Assert.Equal(new WiredRoomForwarding.Destination(42, 17), WiredRoomForwarding.Resolve([tele], "99", id => id == 17 ? 42u : 0, id => id == 2 ? 17u : 0, TestLogging.Logger));
        Assert.Equal(new WiredRoomForwarding.Destination(99), WiredRoomForwarding.Resolve([], "99", _ => 0, _ => 0, TestLogging.Logger));
        Assert.Null(WiredRoomForwarding.Resolve([], "2147483648", _ => 0, _ => 0, TestLogging.Logger));
        link.ExtraData = new LegacyDataFormat { Data = "{\"room_linker\":{\"RoomId\":\"bad\",\"ItemId\":[]}}" };
        Assert.Equal(new WiredRoomForwarding.Destination(99), WiredRoomForwarding.Resolve([link], "99", _ => throw new Exception(), _ => throw new Exception(), TestLogging.Logger));
    }

    [Fact]
    public void ConfiguredRoomForwardingSendsActualClientPacketAndLeavesRoomChecksToEntry()
    {
        using var fixture = new TeleportFixture(); var sent = 0;
        fixture.Habbo.Client.SendCallback = _ => { sent++; return true; };
        var action = ActionBox(fixture.Room, "wf_act_teleport_to_room");
        Assert.True(action.TryValidateConfiguration(new() { IntParams = [0, 100], Text = "42" }, out var config, out _));
        action.ApplyConfiguration(config);
        var context = Context(fixture.Room, new(WiredEventKind.Enter) { Actor = fixture.User }, [], [fixture.User]);
        context.Triggering.UserIds.Add(fixture.User.VirtualId);
        Assert.True(action.Execute(context));
        Assert.Equal(1, sent); Assert.Same(fixture.Room, fixture.Habbo.CurrentRoom); Assert.False(fixture.Habbo.IsTeleporting);
        Assert.Equal(42u, fixture.Habbo.WiredRoomNetworkDestination);
        fixture.Habbo.CurrentRoom = null;
        Assert.False(action.Execute(context));
        Assert.Equal(1, sent);
    }

    [Fact]
    public void ActualForwardingRecordsNetworkEntryOnlyAfterPacketSendAndDestinationConsumesIt()
    {
        using var f = new TeleportFixture();
        var action = ActionBox(f.Room, "wf_act_teleport_to_room");
        Assert.True(action.TryValidateConfiguration(new() { IntParams = [0, 100], Text = "42" }, out var config, out _));
        action.ApplyConfiguration(config);
        var context = Context(f.Room, new(WiredEventKind.Enter) { Actor = f.User }, [], [f.User]);
        context.Triggering.UserIds.Add(f.User.VirtualId);
        f.Habbo.CurrentRoom = null;
        Assert.False(action.Execute(context)); Assert.Equal(0u, f.Habbo.WiredRoomNetworkDestination);
        f.Habbo.CurrentRoom = f.Room;
        f.Habbo.Client.SendCallback = _ => throw new IOException("forward enqueue failed");
        Assert.Throws<IOException>(() => action.Execute(context));
        Assert.Equal(0u, f.Habbo.WiredRoomNetworkDestination);
        f.Habbo.Client.SendCallback = _ => true;
        Assert.True(action.Execute(context));
        Assert.Equal(42u, f.Habbo.WiredRoomNetworkDestination);
        var (destination, _, _) = World(); destination.Id = 42;
        f.Habbo.Gender = "M"; f.Habbo.Look = "test"; f.Habbo.Motto = "";
        f.Habbo.HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);
        f.Habbo.Access = EditorTestSupport.Access([]);
        Assert.True(destination.GetRoomUserManager().AddAvatarToRoom(f.Habbo.Client));
        var joined = Assert.IsType<RoomUser>(destination.GetRoomUserManager().GetRoomUserByHabbo(f.Habbo.Id));
        Assert.NotSame(f.User, joined);
        Assert.Equal(new WiredRoomEntrySnapshot(WiredRoomEntryMethod.RoomNetwork, 0), joined.WiredRoomEntry);
        Assert.Equal(0u, f.Habbo.WiredRoomNetworkDestination);
        Assert.Equal(default, WiredRoomEntrySnapshot.Capture(destination, f.Habbo));
    }

    [Fact]
    public void RealScoreControllerPublishesPreviousValuesAndDistinctGameQuotas()
    {
        var (room, _, _) = World(); var state = new WiredGameState(); var scores = new List<WiredRuntimeEvent>();
        Assert.True(state.GiveScore(room, 1, 7, Team.Red, 10, 1, scores.Add));
        Assert.False(state.GiveScore(room, 1, 7, Team.Red, 10, 1, scores.Add));
        Assert.True(state.GiveScore(room, 1, 8, Team.Red, -20, 1, scores.Add));
        Assert.Equal(10, scores[1].PreviousValue); Assert.Equal(0, scores[1].Value); Assert.Equal(0, room.GetGameManager().Points[1]);
        Assert.True(state.GiveScore(room, 2, 7, Team.Red, 2, 1, scores.Add));
        state.ResetQuotas(); Assert.True(state.GiveScore(room, 1, 7, Team.Red, 3, 1, scores.Add));
        for (var index = 0; index < 12; index++) Assert.True(state.GiveScore(room, 3, 7, Team.Red, 1, null, scores.Add));
        Assert.Equal(17, room.GetGameManager().Points[1]);
    }

    [Fact]
    public void BotArrivalUsesActualRoomIdentityAndOnlyFiresOnce()
    {
        var (room, _, items) = World(); var bot = Bot(room, 7); var target = MakeItem(1, "test"); items[1] = target;
        RoomUsers(room)[7] = bot; var targets = new WiredBotTargets(); targets.Walk(bot, target);
        bot.SetPos(2, 2, 0); Assert.Empty(targets.Poll(room));
        bot.SetPos(0, 0, 0); Assert.Equal(WiredEventKind.BotReachedFurni, Assert.Single(targets.Poll(room)).Kind);
        Assert.Empty(targets.Poll(room)); Assert.False(targets.HasTargets);
        targets.Walk(bot, target); items[1] = MakeItem(1, "replacement");
        Assert.Empty(targets.Poll(room)); Assert.False(targets.HasTargets);
        var user = new RoomUser(1, 0, 8, room) { X = 1, Y = 0 }; RoomUsers(room)[8] = user;
        targets.Follow(bot, user); Assert.Same(user, Assert.Single(targets.Poll(room)).TargetUser); Assert.Empty(targets.Poll(room));
        RoomUsers(room)[8] = new RoomUser(1, 0, 8, room); Assert.Empty(targets.Poll(room)); Assert.False(targets.HasTargets);
    }

    [Fact]
    public void BotValidationPreservesWidthAndFigureChecksActualTurboShape()
    {
        Assert.True(WiredBotActions.TryValidate("wf_act_bot_talk_to_avatar", new() { IntParams = [1, 11, 100, 2], Text = "Alice\tHello" }, out var config, out _));
        Assert.Equal(11, config.UserSources["users"]); Assert.Equal(100, config.UserSources["bots"]); Assert.Equal(2, config.IntParams[3]);
        Assert.True(WiredBotActions.FigureWellFormed("hd-180-1.ch-210-66")); Assert.False(WiredBotActions.FigureWellFormed("hd-180-script"));
    }

    [Fact]
    public void ActualTeleportRestoresImmediatelyWhenSharedQueueCannotAcceptCleanup()
    {
        using var f = new TeleportFixture(2);
        f.Fire(); Assert.Equal(8, f.User.CurrentEffect); Assert.Equal(new Point(0, 0), f.User.Coordinate);
        f.Advance(500); Assert.Equal(new Point(1, 1), f.User.Coordinate); Assert.Equal(8, f.User.CurrentEffect); Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData("target")] [InlineData("source")] [InlineData("save")] [InlineData("visit")]
    public void ActualTeleportCancelsOrRejectsRemovedTargetSourceAndAvatarVisit(string change)
    {
        using var f = new TeleportFixture(); f.Fire(); Assert.Equal(4, f.User.CurrentEffect);
        if (change == "target") f.Items.TryRemove(f.Target.Id, out _);
        if (change == "source") f.Engine.Remove(f.Trigger.Item.Id);
        if (change == "save") Assert.True(f.Engine.PublishConfigured(f.Action, f.Action.Configuration with { IntParams = [1, 100, 0] }, () => { }));
        if (change == "visit") { RoomUsers(f.Room)[7] = new RoomUser(1, 0, 7, f.Room); f.Habbo.Effects.CurrentEffect = -1; }
        f.Advance(1500); Assert.Equal(new Point(0, 0), f.User.Coordinate);
        Assert.Equal(change == "visit" ? -1 : 8, f.Habbo.Effects.CurrentEffect); Assert.Empty(f.Errors);
    }

    [Fact]
    public void ActualOverlappingTeleportsAndLaterEffectChangesPreserveTheRightEffect()
    {
        using var f = new TeleportFixture(); f.Fire(); f.Fire(); Assert.Equal(4, f.User.CurrentEffect);
        f.Advance(1500); Assert.Equal(8, f.User.CurrentEffect);
        f.User.SetPos(0, 0, 0); f.Fire(); f.Habbo.Effects.ApplyEffect(12); f.Advance(1500); Assert.Equal(12, f.User.CurrentEffect);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void FloorPlacementAndRemovalAttachAndDetachActualCounterController()
    {
        using var fixture = new TeleportFixture();
        var counter = MakeItem(500, "wf_upcounter1");
        typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(counter, fixture.Room);
        var databaseField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = databaseField.GetValue(null);
        var database = DispatchProxy.Create<IDatabase, RecordingProxy>();
        var adapter = DispatchProxy.Create<IQueryAdapter, RecordingProxy>();
        ((RecordingProxy)(object)database).InvokeMethod = (method, _) => method.Name == "GetQueryReactor" ? adapter : null;
        try
        {
            databaseField.SetValue(null, database);
            Assert.True(fixture.Room.GetRoomItemHandler().SetFloorItem(null!, counter, 2, 2, 0, true, false, false));
            Assert.True(fixture.Room.GetWired().TryUseCounter(counter, 0));
            fixture.Room.GetRoomItemHandler().RemoveFurniture(null!, counter.Id);
            Assert.Null(fixture.Room.GetRoomItemHandler().GetItem(counter.Id));
            Assert.False(fixture.Room.GetWired().TryUseCounter(counter, 0));
        }
        finally { databaseField.SetValue(null, original); }
    }

    private static ConcurrentDictionary<int, RoomUser> RoomUsers(Room room) =>
        (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomUserManager())!;
    private static RoomUser Bot(Room room, int virtualId)
    {
        var bot = new RoomUser(0, 0, virtualId, room) { BotData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot)) };
        bot.BotData.Name = "Alice"; bot.BotData.Look = "hd-180-1"; bot.BotData.Gender = "M"; return bot;
    }
    private sealed class TeleportFixture : IDisposable
    {
        public readonly Room Room; public readonly ConcurrentDictionary<uint, Item> Items;
        public readonly RoomUser User; public readonly Habbo Habbo; public readonly Item Target;
        public readonly WiredModernAction Action; public readonly WiredModernTrigger Trigger;
        public readonly WiredStackEngine Engine; public readonly List<Exception> Errors = [];
        public IItemDataManager? DefinitionManager;
        private readonly object? _originalGame; private long _now;
        public TeleportFixture(int cap = 100)
        {
            (Room, _, Items) = World();
            var gameField = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
            _originalGame = gameField.GetValue(null);
            var clients = new GameClientManager(null!, null!); var game = DispatchProxy.Create<IGame, RecordingProxy>();
            ((RecordingProxy)(object)game).InvokeMethod = (method, _) => method.Name == "get_ClientManager" ? clients : method.Name == "get_ItemManager" ? DefinitionManager : null;
            gameField.SetValue(null, game);
            var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
            {
                Revision = new Revision { InternalIdToOutgoingIdMapping = typeof(ServerPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(field => field.FieldType == typeof(uint)).Select(field => (uint)field.GetValue(null)!).Distinct().ToDictionary(id => id, id => id) },
                SendCallback = _ => true
            };
            Habbo = (Habbo)RuntimeHelpers.GetUninitializedObject(typeof(Habbo)); Habbo.Id = 1; Habbo.Username = "Alice"; Habbo.CurrentRoom = Room;
            Habbo.Client = client; Habbo.Effects = new(); typeof(EffectsComponent).GetField("_habbo", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Habbo.Effects, Habbo);
            Habbo.Effects.CurrentEffect = 8; client.SetHabbo(Habbo); clients.RegisterClient(client, 1, "Alice");
            User = new(1, 0, 7, Room); RoomUsers(Room)[7] = User;
            Room.GetGameMap().AddUserToMap(User, new(0, 0));
            var wired = new WiredComponent(Room, TestLogging.Logger);
            Engine = new(() => _now, box => Items.TryGetValue(box.Item.Id, out var item) && ReferenceEquals(item, box.Item), _ => true, _ => { }, Errors.Add, new() { MaxPendingStacks = cap });
            Engine.BindRuntime(Room, new(() => Items.Values, () => RoomUsers(Room).Values), wired);
            typeof(WiredComponent).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(wired, Engine);
            typeof(Room).GetField("_wiredComponent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Room, wired);
            Target = MakeItem(1, "test"); Target.SetState(1, 1, 0, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0)); Items[1] = Target;
            Trigger = new(Room, MakeItem(101, "wf_trg_enter_room"), Descriptor("wf_trg_enter_room")); Trigger.ApplyConfiguration(WiredTriggerConfiguration.Defaults("wf_trg_enter_room"));
            Action = new(Room, MakeItem(100, "wf_act_teleport_to"), Descriptor("wf_act_teleport_to"), new(),
                evt => wired.Dispatch(evt), wired.DispatchWalkTransition, new(), TestLogging.Logger);
            Action.TryValidateConfiguration(new() { IntParams = [0, 100, 0], SelectedItems = [1] }, out var config, out _); Action.ApplyConfiguration(config);
            Items[101] = Trigger.Item; Items[100] = Action.Item; Engine.Add(Trigger); Engine.Add(Action);
        }
        public void Fire() => Assert.True(Engine.Dispatch(new WiredRuntimeEvent(WiredEventKind.Enter) { Actor = User }));
        public void Advance(int milliseconds) { _now += milliseconds; Engine.OnFastCycle(); }
        public void Dispose() { Engine.Clear(); typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, _originalGame); }
    }
    [Fact]
    public void ClockTicksAtHalfSecondsAndDisplaysOnlyWholeSeconds()
    {
        var item = MakeItem(1, "wf_upcounter1");
        var clocks = new WiredCounterController();
        Assert.True(clocks.Attach(item));
        Assert.True(clocks.Control(item, 0, 100));
        Assert.Empty(clocks.Poll(599));
        var half = Assert.Single(clocks.Poll(600));
        Assert.Equal(500, half.Event.Value);
        Assert.False(half.DisplayChanged);
        var whole = Assert.Single(clocks.Poll(1100));
        Assert.Equal(500, whole.Event.PreviousValue);
        Assert.Equal(1000, whole.Event.Value);
        Assert.Equal("1", item.LegacyDataString);
        Assert.True(whole.DisplayChanged);
        Assert.Single(clocks.Poll(10000)); // Late polls never burst overdue ticks.
        Assert.Equal(1500, clocks.ReadMilliseconds(item));
    }

    [Fact]
    public void ClockAdjustTranslatesPolarisOperatorsAndClampsAtBothBounds()
    {
        var item = MakeItem(1, "wf_upcounter2");
        var clocks = new WiredCounterController(10);
        clocks.Attach(item);
        Assert.True(clocks.Adjust(item, 2, 0, 7));
        Assert.Equal(3500, clocks.ReadMilliseconds(item));
        clocks.Adjust(item, 0, 0, 5);
        Assert.Equal(5000, clocks.ReadMilliseconds(item));
        clocks.Adjust(item, 1, 0, 119);
        Assert.Equal(0, clocks.ReadMilliseconds(item));
        Assert.False(clocks.Adjust(item, 3, 0, 0));
    }

    [Fact]
    public void ClockStopResetRestartAndRemovalHaveActualLifecycleState()
    {
        var item = MakeItem(1, "wf_upcounter1");
        var clocks = new WiredCounterController();
        clocks.Attach(item);
        clocks.Control(item, 0, 0);
        clocks.Poll(500);
        clocks.Control(item, 1, 500);
        Assert.Empty(clocks.Poll(5000));
        Assert.Equal(500, clocks.ReadMilliseconds(item));
        clocks.Control(item, 3, 5000);
        Assert.True(clocks.IsRunning(item));
        Assert.Equal(0, clocks.ReadMilliseconds(item));
        clocks.TakeChanges();
        Assert.Equal(500, Assert.Single(clocks.Poll(5500)).Event.Value);
        clocks.Control(item, 2, 5500);
        Assert.False(clocks.IsRunning(item));
        clocks.Forget(item);
        Assert.Null(clocks.ReadMilliseconds(item));
        Assert.Empty(clocks.Poll(10000));
        Assert.False(clocks.Control(item, 0, 10000));
    }

    [Fact]
    public void GameCounterCountsDownAndPublishesStartEndExactlyOnce()
    {
        var item = MakeItem(1, "wf_game_upcounter1");
        item.LegacyDataString = "2";
        var clocks = new WiredCounterController();
        clocks.Attach(item);
        Assert.True(clocks.Use(item, 0, 0));
        Assert.Equal(WiredEventKind.GameStart, Assert.Single(clocks.TakeChanges()).Event.Kind);
        Assert.False(clocks.Control(item, 0, 0)); // Wired clock controls target upcounters, not game countdowns.
        Assert.Empty(clocks.Poll(999));
        Assert.Equal("1", Assert.Single(clocks.Poll(1000)).Item.LegacyDataString);
        Assert.Equal(new[] { WiredEventKind.StateChanged, WiredEventKind.GameEnd }, clocks.Poll(2000).Select(change => change.Event.Kind));
        Assert.False(clocks.IsRunning(item));
        Assert.Empty(clocks.Poll(3000));
        clocks.Use(item, 2, 3000);
        Assert.Equal("60", item.LegacyDataString);
        Assert.Equal(WiredEventKind.StateChanged, Assert.Single(clocks.TakeChanges()).Event.Kind);
    }

    [Fact]
    public void ControllerRejectsUnrelatedItemsAndReusedIds()
    {
        var clocks = new WiredCounterController();
        Assert.False(clocks.Attach(MakeItem(1, "football_counter")));
        var original = MakeItem(1, "wf_upcounter1");
        clocks.Attach(original);
        Assert.Null(clocks.ReadMilliseconds(MakeItem(1, "wf_upcounter1")));
    }

    [Fact]
    public void ConditionUsesClickedAvatarSourceAndPolarisActionNumbers()
    {
        var (room, _, _) = World();
        var user = new RoomUser(1, 0, 7, room);
        user.SetStatus("sit");
        var context = Context(room, new(WiredEventKind.ClickUser) { TargetUser = user }, [], [user]);
        var box = new WiredModernCondition(room, MakeItem(100, "wf_cnd_user_performs_action"), Descriptor("wf_cnd_user_performs_action"), _ => null, () => DateTimeOffset.UtcNow);
        Assert.True(box.TryValidateConfiguration(new() { IntParams = [6, 0, 0, 0, 1, 11, 0] }, out var config, out _));
        box.ApplyConfiguration(config);
        Assert.True(box.Execute(context));
        user.RemoveStatus("sit");
        Assert.False(box.Execute(context));
    }

    [Fact]
    public void ConditionConfigurationKeepsPrimaryAndComparisonRoles()
    {
        var proposed = new WiredConfiguration { IntParams = [100, 201, 0], Text = "5;6", SelectedItems = [1] };
        Assert.True(WiredConditionConfiguration.TryValidate("wf_cnd_stuff_is", proposed, out var result, out _));
        Assert.Equal(100, result.FurniSources["items"]);
        Assert.Equal(201, result.FurniSources["comparison"]);
        Assert.Equal(new uint[] { 5, 6 }, result.SecondarySelectedItems);
        Assert.Equal(proposed.IntParams, result.IntParams);
        Assert.Equal(proposed.Text, result.Text);
    }

    [Fact]
    public void SnapshotPreparationCapturesOnlyPickedAttachedItemsWithoutMutatingLiveConfig()
    {
        var (room, _, items) = World();
        var picked = MakeItem(1, "test");
        picked.LegacyDataString = "state,with;delimiters";
        items[1] = picked;
        items[2] = MakeItem(2, "test");
        var box = new WiredModernCondition(room, MakeItem(100, "wf_cnd_match_snapshot"), Descriptor("wf_cnd_match_snapshot"), _ => null, () => DateTimeOffset.UtcNow);
        var candidate = new WiredConfiguration { IntParams = [1, 1, 1, 1, 100, 0], SelectedItems = [1] };
        var prepared = WiredRoomOperations.PrepareSnapshots(box, candidate);
        Assert.Equal("state,with;delimiters", Assert.Single(prepared.Snapshots).State);
        Assert.Empty(candidate.Snapshots);
        Assert.Empty(box.Configuration.Snapshots);
    }

    [Fact]
    public void TriggerSeparatesUseAndStateMutationAndUsesStoredSnapshot()
    {
        var (room, _, _) = World();
        var item = MakeItem(1, "test");
        var box = new WiredModernTrigger(room, MakeItem(100, "wf_trg_state_changed"), Descriptor("wf_trg_state_changed"));
        box.TryValidateConfiguration(new() { IntParams = [1, 100], SelectedItems = [1], Snapshots = [WiredRoomOperations.Capture(item)] }, out var config, out _);
        box.ApplyConfiguration(config);
        Assert.False(box.Execute(Context(room, new(WiredEventKind.Use) { EventItem = item }, [item], [])));
        Assert.True(box.Execute(Context(room, new(WiredEventKind.StateChanged) { EventItem = item }, [item], [])));
        item.LegacyDataString = "1";
        Assert.False(box.Execute(Context(room, new(WiredEventKind.StateChanged) { EventItem = item }, [item], [])));
    }

    [Fact]
    public void TimedContextualTriggerResetsItsOneShotEpoch()
    {
        var (room, _, _) = World();
        var box = new WiredModernTimedTrigger(room, MakeItem(100, "wf_trg_at_time_long"), Descriptor("wf_trg_at_time_long"));
        box.TryValidateConfiguration(new() { IntParams = [1] }, out var config, out _);
        box.ApplyConfiguration(config);
        box.Reset(1000);
        Assert.Null(box.Poll(5999));
        Assert.Equal(WiredEventKind.Elapsed, box.Poll(6000)!.Kind);
        Assert.Null(box.Poll(11000));
        box.Reset(11000);
        Assert.NotNull(box.Poll(16000));
    }

    [Fact]
    public void FullPlacementHonoursScopedUsersAndPreservesOrdinaryRejection()
    {
        var (room, map, items) = World();
        var mover = MakeItem(1, "test");
        mover.SetState(0, 0, 0, Gamemap.GetAffectedTiles(1, 1, 0, 0, 0));
        items[1] = mover;
        map.AddToMap(mover);
        var occupant = new RoomUser(1, 0, 7, room) { X = 1, Y = 1 };
        map.AddUserToMap(occupant, new(1, 1));
        Assert.False(room.GetRoomItemHandler().SetFloorItem(null!, mover, 1, 1, 0, false, false, false));
        Assert.False(WiredRoomOperations.CanMoveItem(room, mover, 1, 1, 0, collision: new(new HashSet<uint>(), new HashSet<int> { 8 }, new HashSet<uint>())));
        var allowed = new WiredCollisionPolicy(new HashSet<uint>(), new HashSet<int> { 7 }, new HashSet<uint>());
        Assert.True(WiredRoomOperations.CanMoveItem(room, mover, 1, 1, 0, collision: allowed));
        var databaseField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = databaseField.GetValue(null);
        var queries = new List<string>();
        var db = DispatchProxy.Create<IDatabase, RecordingProxy>();
        var adapter = DispatchProxy.Create<IQueryAdapter, RecordingProxy>();
        ((RecordingProxy)(object)adapter).InvokeMethod = (method, args) => { if (method.Name == "RunQuery" && args?.Length == 1) queries.Add((string)args[0]!); return null; };
        ((RecordingProxy)(object)db).InvokeMethod = (method, _) => method.Name == "GetQueryReactor" ? adapter : null;
        try
        {
            databaseField.SetValue(null, db);
            Assert.True(room.GetRoomItemHandler().SetFloorItem(null!, mover, 1, 1, 0, false, false, false, wiredCollision: allowed));
            Assert.Equal(new Point(1, 1), new Point(mover.GetX, mover.GetY));
            Assert.DoesNotContain(mover, map.GetCoordinatedItems(new(0, 0)));
            Assert.Contains(mover, map.GetCoordinatedItems(new(1, 1)));
            Assert.Contains(queries, query => query.Contains("UPDATE `items`"));
        }
        finally { databaseField.SetValue(null, original); }
    }

    [Fact]
    public void PhysicsScopeNeverPermitsAnExplicitBlockingFurnitureItem()
    {
        var (room, map, items) = World();
        var mover = MakeItem(1, "test");
        items[1] = mover;
        var obstacle = MakeItem(2, "test");
        obstacle.SetState(1, 1, 0, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0));
        items[2] = obstacle;
        map.AddToMap(obstacle);
        var blocked = new WiredCollisionPolicy(new HashSet<uint> { 2 }, new HashSet<int>(), new HashSet<uint> { 2 });
        Assert.False(WiredRoomOperations.CanMoveItem(room, mover, 1, 1, 0, collision: blocked));
        Assert.False(room.GetRoomItemHandler().SetFloorItem(null!, mover, 1, 1, 0, false, false, false, wiredCollision: blocked));
    }

    [Fact]
    public void AvatarMovementHonoursScopedThroughUsersAndExplicitBlockingItems()
    {
        using var fixture = new TeleportFixture(); var room = fixture.Room; var actor = fixture.User;
        var occupant = new RoomUser(2, 0, 8, room); occupant.SetPos(1, 0, 0); RoomUsers(room)[8] = occupant;
        room.GetGameMap().AddUserToMap(occupant, new(1, 0));
        var action = ActionBox(room, "wf_act_move_rotate_user");
        Assert.True(action.TryValidateConfiguration(new() { IntParams = [2, -1, 0] }, out var config, out _)); action.ApplyConfiguration(config);
        WiredRuntimeContext Firing() { var c = Context(room, new(WiredEventKind.Enter) { Actor = actor }, fixture.Items.Values.ToArray(), RoomUsers(room).Values.ToArray()); c.Triggering.UserIds.Add(actor.VirtualId); c.Policy.Addons.DisableAnimation = true; return c; }
        Assert.False(action.Execute(Firing())); Assert.Equal(0, actor.X);
        var wrong = Firing(); wrong.Policy.Addons.Physics = new(false, new HashSet<uint>(), new HashSet<int> { 9 }, new HashSet<uint>());
        Assert.False(action.Execute(wrong)); Assert.Equal(0, actor.X);
        var blocked = MakeItem(10, "test"); blocked.SetState(1, 0, 0, Gamemap.GetAffectedTiles(1, 1, 1, 0, 0)); fixture.Items[10] = blocked; room.GetGameMap().AddToMap(blocked);
        var context = Firing(); context.Policy.Addons.Physics = new(false, new HashSet<uint>(), new HashSet<int> { 8 }, new HashSet<uint> { 10 });
        Assert.False(action.Execute(context)); Assert.Equal(0, actor.X);
        context.Policy.Addons.Physics = new(false, new HashSet<uint>(), new HashSet<int> { 8 }, new HashSet<uint>());
        Assert.True(action.Execute(context)); Assert.Equal(1, actor.X); Assert.Equal(0, actor.Y);
    }

    [Fact]
    public void OptionalUnnamedBotHandItemGrantsWithoutBotButExplicitMissingBotRejects()
    {
        using var fixture = new TeleportFixture(); var action = ActionBox(fixture.Room, "wf_act_bot_give_handitem");
        var config = new WiredConfiguration { IntParams = [2, 0, 0], Text = "" };
        Assert.True(action.TryValidateConfiguration(config, out var valid, out _)); action.ApplyConfiguration(valid);
        var context = Context(fixture.Room, new(WiredEventKind.Enter) { Actor = fixture.User }, fixture.Items.Values.ToArray(), [fixture.User]);
        context.Triggering.UserIds.Add(fixture.User.VirtualId);
        Assert.True(action.Execute(context)); Assert.Equal(2, fixture.User.CarryItemId);
        Assert.True(action.TryValidateConfiguration(config with { IntParams = [3, 0, 100], Text = "missing" }, out valid, out _)); action.ApplyConfiguration(valid);
        Assert.False(action.Execute(context)); Assert.Equal(2, fixture.User.CarryItemId);
        var bot = Bot(fixture.Room, 8); RoomUsers(fixture.Room)[8] = bot;
        Assert.True(action.TryValidateConfiguration(config with { IntParams = [4, 200, 0] }, out valid, out _)); action.ApplyConfiguration(valid);
        context = Context(fixture.Room, new(WiredEventKind.Enter) { Actor = bot }, fixture.Items.Values.ToArray(), [fixture.User, bot]);
        context.SelectorPool.UserIds.Add(fixture.User.VirtualId);
        Assert.True(action.Execute(context)); Assert.Equal(4, fixture.User.CarryItemId);
    }

    [Fact]
    public void TemporaryPlacementMoveRemoveUsesRealMapsWithoutPersistence()
    {
        var (room, map, items) = World();
        var handler = room.GetRoomItemHandler();
        var definition = MakeItem(1, "test").Definition; definition.Stackable = true;
        var databaseField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = databaseField.GetValue(null);
        var database = DispatchProxy.Create<IDatabase, RecordingProxy>();
        ((RecordingProxy)(object)database).InvokeMethod = (method, _) => throw new InvalidOperationException("Temporary path opened SQL: " + method.Name);
        try
        {
            databaseField.SetValue(null, database);
            var item = Assert.IsType<Item>(handler.PlaceTemporaryFloorItem(definition, 1, 0, 0, 0, 0, "1"));
            Assert.True(item.IsTemporary); Assert.True(handler.OwnsTemporary(item)); Assert.Equal(-1, unchecked((int)item.Id));
            Assert.Same(room, item.GetRoom()); Assert.Same(item, items[item.Id]); Assert.Equal("1", item.LegacyDataString);
            Assert.Contains(item, map.GetCoordinatedItems(new(0, 0)));
            Assert.True(WiredRoomOperations.MoveItem(room, item, 1, 1, animate: false));
            Assert.DoesNotContain(item, map.GetCoordinatedItems(new(0, 0))); Assert.Contains(item, map.GetCoordinatedItems(new(1, 1)));
            Assert.True(handler.RemoveTemporaryFloorItem(item)); Assert.False(handler.OwnsTemporary(item));
            Assert.False(items.ContainsKey(item.Id)); Assert.DoesNotContain(item, map.GetCoordinatedItems(new(1, 1)));
            Assert.False(handler.RemoveTemporaryFloorItem(item));
            var replacement = Assert.IsType<Item>(handler.PlaceTemporaryFloorItem(definition, 1, 0, 0, 0));
            Assert.Equal(-2, unchecked((int)replacement.Id));
            Assert.False(handler.RemoveTemporaryFloorItem(MakeItem(replacement.Id, "test")));
            handler.UpdateItem(replacement);
            handler.Dispose(); // No temporary extra-data or coordinate SQL during unload.
        }
        finally { databaseField.SetValue(null, original); }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void ActualPlacementReadiesFloorAndWallFxOnlyAfterSuccessfulAnnouncement(bool wall, bool enqueue)
    {
        using var f = new TeleportFixture();
        f.Habbo.Gender = "M"; f.Habbo.Motto = ""; f.Habbo.Look = "test";
        f.Habbo.HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);
        f.Room.SendObjects(f.Habbo.Client);
        Assert.Single(f.Room.GetWired().CaptureFxViewers());
        f.Habbo.Client.SendCallback = _ => enqueue ? true : throw new IOException("placement enqueue failed");
        var databaseField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = databaseField.GetValue(null);
        var query = DispatchProxy.Create<IQueryAdapter, RecordingProxy>();
        var database = DispatchProxy.Create<IDatabase, RecordingProxy>();
        ((RecordingProxy)(object)database).InvokeMethod = (method, _) => method.Name == "GetQueryReactor" ? query : null;
        try
        {
            databaseField.SetValue(null, database);
            Item placed;
            if (wall)
            {
                placed = MakeItem(50, "wall"); placed.Definition.Type = ItemType.Wall;
                placed.WallCoordinates = ":w=1,1 l=10,20 l"; placed.Username = "Alice";
                typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(placed, f.Room);
                Assert.True(f.Room.GetRoomItemHandler().SetWallItem(f.Habbo.Client, placed));
            }
            else
            {
                placed = Assert.IsType<Item>(f.Room.GetRoomItemHandler().PlaceTemporaryFloorItem(MakeItem(50, "floor").Definition, 1, 2, 2, 0));
            }
            Assert.Same(placed, f.Room.GetRoomItemHandler().GetItem(placed.Id));
            var ready = Assert.Single(f.Room.GetWired().CaptureFxViewers()).ReadyHolders;
            Assert.Equal(enqueue, ready.Contains(WiredVariableRuntimeFrames.FurniHolder(placed)));
        }
        finally { databaseField.SetValue(null, original); }
    }

    [Fact]
    public void TemporaryPlacementChecksFullFootprintHeightAndRoomLimit()
    {
        var (room, map, _) = World(); var handler = room.GetRoomItemHandler();
        var wide = MakeItem(1, "test").Definition; wide.Length = 2; wide.Stackable = true;
        Assert.Null(handler.PlaceTemporaryFloorItem(wide, 1, 2, 2, 0));
        var footprint = Gamemap.GetAffectedTiles(wide.Length, wide.Width, 0, 0, 0).Values.First();
        map.Model.SqState[footprint.X, footprint.Y] = SquareState.Blocked;
        Assert.Null(handler.PlaceTemporaryFloorItem(wide, 1, 0, 0, 0, 0));
        map.Model.SqState[footprint.X, footprint.Y] = SquareState.Open;
        var definition = MakeItem(1, "test").Definition; definition.Stackable = true; definition.Height = 1;
        Assert.Null(handler.PlaceTemporaryFloorItem(definition, 1, 0, 0, 0, 80));
        definition.Height = 0;
        for (var i = 0; i < RoomItemHandling.TemporaryItemLimit; i++)
            Assert.NotNull(handler.PlaceTemporaryFloorItem(definition, 1, 0, 0, 0, 0));
        Assert.Null(handler.PlaceTemporaryFloorItem(definition, 1, 0, 0, 0));
        Assert.Equal(RoomItemHandling.TemporaryItemLimit, handler.GetFloor.Count);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void TemporaryRemovalEditorModesProtectPermanentAndMismatchedIdentities(int mode)
    {
        using var f = new TeleportFixture(); var handler = f.Room.GetRoomItemHandler(); var def = MakeItem(3, "test").Definition; def.Stackable = true;
        var permanent = MakeItem(uint.MaxValue, "test"); f.Items[permanent.Id] = permanent;
        var item = Assert.IsType<Item>(handler.PlaceTemporaryFloorItem(def, 1, 2, 2, 0));
        Assert.Equal(-2, unchecked((int)item.Id)); // A real permanent high-uint item owns the -1 bit pattern.
        var action = ActionBox(f.Room, "wf_act_remove_furni");
        Assert.True(action.TryValidateConfiguration(new() { IntParams = [mode, 0] }, out var config, out _)); action.ApplyConfiguration(config);
        var ctx = Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User]); ctx.Triggering.FurniIds.UnionWith([permanent.Id, item.Id]);
        Assert.True(action.Execute(ctx)); Assert.Same(permanent, handler.GetItem(permanent.Id)); Assert.Null(handler.GetItem(item.Id));
        Assert.False(action.Execute(ctx));
    }

    [Fact]
    public void SnapshotPlacementCopiesDetachedTemplatesWithRelativeGeometryAndAltitude()
    {
        using var f = new TeleportFixture(); var manager = DispatchProxy.Create<IItemDataManager, RecordingProxy>();
        var def = MakeItem(5, "test").Definition; def.Id = 5; def.Stackable = true;
        ((RecordingProxy)(object)manager).InvokeMethod = (m, _) => m.Name == "get_Items" ? new Dictionary<uint, ItemDefinition> { [5] = def } : null;
        f.DefinitionManager = manager; f.Target.Definition.Stackable = true;
        var action = ActionBox(f.Room, "wf_act_place_furni");
        var proposed = WiredTemporaryFurnitureActions.Defaults("wf_act_place_furni") with {
            TemporaryPlacement = new(Location: WiredPlaceLocationType.CustomLocation, Altitude: WiredPlaceAltitudeType.CustomAltitude, OffsetAltitudeHundredths: 125),
            SecondarySelectedItems = [1], Snapshots = [new(900, 5, 7, 7, 3, 0, "1"), new(901, 5, 8, 7, 4, 2, "0")]
        };
        Assert.True(action.TryValidateConfiguration(proposed, out var config, out _)); action.ApplyConfiguration(config);
        var context = Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User]);
        Assert.True(action.Execute(context));
        var copies = f.Room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary).OrderBy(item => item.GetX).ToArray();
        Assert.Equal(2, copies.Length); Assert.Equal((1, 1, 1.25, "1"), (copies[0].GetX, copies[0].GetY, copies[0].GetZ, copies[0].LegacyDataString));
        Assert.Equal((2, 1, 1.25, 2), (copies[1].GetX, copies[1].GetY, copies[1].GetZ, copies[1].Rotation));
        var prepared = WiredRoomOperations.PrepareSnapshots(action, proposed with { SelectedItems = [copies[0].Id], SecondarySelectedItems = [copies[1].Id] });
        Assert.Empty(prepared.SelectedItems); Assert.Empty(prepared.SecondarySelectedItems);
        var template = Assert.Single(prepared.Snapshots); Assert.Equal(5u, template.DefinitionId); Assert.Equal("1", template.State);
        Assert.Equal(prepared.Snapshots, WiredRoomOperations.PrepareSnapshots(action, prepared).Snapshots);
        Assert.True(f.Room.GetRoomItemHandler().RemoveTemporaryFloorItem(copies[0]));
        Assert.True(action.TryValidateConfiguration(prepared with { TemporaryPlacement = new(Altitude: WiredPlaceAltitudeType.SourceAltitude) }, out config, out _)); action.ApplyConfiguration(config);
        Assert.True(action.Execute(Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User])));
    }

    [Fact]
    public void CurrentSixPlacementEditorKeepsQuantityAndAbsoluteLocationMeanings()
    {
        using var f = new TeleportFixture(); var manager = DispatchProxy.Create<IItemDataManager, RecordingProxy>();
        var def = MakeItem(5, "test").Definition; def.Id = 5; def.Stackable = true;
        ((RecordingProxy)(object)manager).InvokeMethod = (m, _) => m.Name == "get_Items" ? new Dictionary<uint, ItemDefinition> { [5] = def } : null; f.DefinitionManager = manager;
        var action = ActionBox(f.Room, "wf_act_place_furni");
        Assert.True(action.TryValidateConfiguration(new() { IntParams = [5, 3, 1, 2, 1, 2] }, out var config, out _)); action.ApplyConfiguration(config);
        Assert.True(action.Execute(Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User])));
        var copies = f.Room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary).ToArray(); Assert.Equal(3, copies.Length);
        Assert.All(copies, item => Assert.Equal((2, 1, 2), (item.GetX, item.GetY, item.Rotation)));
    }

    [Fact]
    public void SnapshotPreparationPreservesSelectedPivotOrderAfterOriginalsDetach()
    {
        using var f = new TeleportFixture(); f.Target.Definition.Stackable = true;
        var definition = MakeItem(5, "test").Definition; definition.Id = 5; definition.Stackable = true;
        var manager = DispatchProxy.Create<IItemDataManager, RecordingProxy>(); ((RecordingProxy)(object)manager).InvokeMethod = (method, _) => method.Name == "get_Items" ? new Dictionary<uint, ItemDefinition> { [5] = definition } : null; f.DefinitionManager = manager;
        var firstInserted = MakeItem(10, "test"); firstInserted.Definition = definition; firstInserted.SetState(1, 1, 0, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0)); f.Items[10] = firstInserted;
        var firstPicked = MakeItem(20, "test"); firstPicked.Definition = definition; firstPicked.SetState(2, 1, 0, Gamemap.GetAffectedTiles(1, 1, 2, 1, 0)); f.Items[20] = firstPicked;
        var action = ActionBox(f.Room, "wf_act_place_furni");
        var proposed = WiredTemporaryFurnitureActions.Defaults("wf_act_place_furni") with { SelectedItems = [20, 10], SecondarySelectedItems = [1], TemporaryPlacement = new(Location: WiredPlaceLocationType.CustomLocation) };
        var captured = WiredRoomOperations.PrepareSnapshots(action, proposed); Assert.Equal(new uint[] { 20, 10 }, captured.Snapshots.Select(snapshot => snapshot.ItemId));
        f.Items.TryRemove(10, out _); f.Items.TryRemove(20, out _);
        Assert.True(action.TryValidateConfiguration(captured, out var config, out _)); action.ApplyConfiguration(config);
        Assert.True(action.Execute(Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User])));
        var copies = f.Room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary).ToArray(); Assert.Equal(2, copies.Length);
        Assert.Contains(copies, item => item.GetX == 1 && item.GetY == 1); Assert.Contains(copies, item => item.GetX == 0 && item.GetY == 1);
        Assert.DoesNotContain(copies, item => item.GetX == 2);
    }

    [Fact]
    public void RewardValidationRejectsUnsupportedCurrenciesAndKeepsActualIntervals()
    {
        var config = WiredRewards.Defaults() with { Text = "1,furni#5,100;0,ABC,50" };
        Assert.True(WiredRewards.TryValidate(config, out _, out _));
        foreach (var code in new[] { "credits#5", "pixels#5", "diamonds#5", "points5#5" })
            Assert.False(WiredRewards.TryValidate(config with { Text = "1," + code + ",100" }, out _, out _));
        Assert.False(WiredRewards.TryValidate(config with { Text = string.Join(';', Enumerable.Repeat("0,A,100", 21)) }, out _, out _));
        var claim = new WiredRewardClaim { Count = 1, LastClaimUnix = 100 };
        Assert.False(WiredRewards.IntervalOpen(claim, 0, 1, long.MaxValue));
        foreach (var pair in new[] { (1, 86400), (2, 3600), (3, 60) })
        {
            Assert.False(WiredRewards.IntervalOpen(claim, pair.Item1, 2, 100 + 2 * pair.Item2 - 1));
            Assert.True(WiredRewards.IntervalOpen(claim, pair.Item1, 2, 100 + 2 * pair.Item2));
        }
    }

    [Fact]
    public void RewardActionDatabaseFailureEmitsNoInventoryOrResultPacket()
    {
        using var f = new TeleportFixture(); f.Habbo.Inventory = new() { Furniture = new([], []), Badges = new(new()) };
        var sent = 0; ((FlashGameClient)f.Habbo.Client).SendCallback = _ => { sent++; return true; };
        var dbField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!; var original = dbField.GetValue(null);
        var database = DispatchProxy.Create<IDatabase, RecordingProxy>(); ((RecordingProxy)(object)database).InvokeMethod = (_, _) => throw new InvalidOperationException("Injected SQL failure");
        try
        {
            dbField.SetValue(null, database); var action = ActionBox(f.Room, "wf_act_give_reward");
            Assert.True(action.TryValidateConfiguration(WiredRewards.Defaults() with { Text = "1,furni#5,100" }, out var config, out _)); action.ApplyConfiguration(config);
            var ctx = Context(f.Room, new(WiredEventKind.Enter) { Actor = f.User }, f.Items.Values.ToArray(), [f.User]); ctx.Triggering.UserIds.Add(f.User.VirtualId);
            Assert.False(action.Execute(ctx)); Assert.Equal(0, sent); Assert.Empty(f.Habbo.Inventory.Furniture.GetItems);
        }
        finally { dbField.SetValue(null, original); }
    }

    [WiredVariableDatabaseFact]
    public async Task ActualRewardSqlSerializesQuotaGrantAndRollbackBeforePublication()
    {
        using var f = new TeleportFixture();
        var connectionString = ModernWiredDatabaseProbe.GuardedConnectionString(); using var admin = new MySqlConnection(connectionString); await admin.OpenAsync();
        Assert.Equal("InnoDB", admin.QuerySingle<string>("SELECT ENGINE FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='wired_reward_state'"));
        var userId = 0u; var roomId = 0u; var boxId = 0u; var badgeCode = "WR" + Guid.NewGuid().ToString("N")[..10];
        try
        {
            userId = ModernWiredDatabaseProbe.Insert(admin, "users", new() { ["username"] = badgeCode, ["password"] = "unused", ["mail"] = badgeCode + "@invalid" });
            roomId = ModernWiredDatabaseProbe.Insert(admin, "rooms", new() { ["owner"] = userId.ToString(), ["caption"] = "Disposable atomic reward probe", ["model_name"] = admin.QueryFirst<string>("SELECT id FROM room_models LIMIT 1") });
            var baseId = admin.QueryFirst<uint>("SELECT id FROM furniture WHERE type='s' LIMIT 1");
            boxId = ModernWiredDatabaseProbe.Insert(admin, "items", new() { ["user_id"] = userId, ["room_id"] = roomId, ["base_item"] = baseId, ["extra_data"] = "", ["wall_pos"] = "" });
            var loadedRows = new DataTable();
            using (var reader = admin.ExecuteReader("SELECT items.*,users.username FROM items JOIN users ON users.id=items.user_id WHERE items.id=@boxId", new { boxId }))
                loadedRows.Load(reader);
            var box = ItemLoader.ReadRoomItem(Assert.Single(loadedRows.Rows.Cast<DataRow>()), roomId, MakeItem(boxId, "wf_act_give_reward").Definition);
            f.Habbo.Id = checked((int)userId); f.Habbo.Inventory = new() { Furniture = new([], []), Badges = new(new()) };
            var sent = 0; ((FlashGameClient)f.Habbo.Client).SendCallback = _ => { sent++; return true; };
            var definition = MakeItem(baseId, "probe_product").Definition; definition.Id = baseId;
            var definitions = DispatchProxy.Create<IItemDataManager, RecordingProxy>();
            ((RecordingProxy)(object)definitions).InvokeMethod = (method, _) => method.Name == "get_Items" ? new Dictionary<uint, ItemDefinition> { [baseId] = definition } : method.Name == "GetItemByName" ? definition : null;
            var database = new ModernWiredDatabaseProbe.ProbeDatabase(connectionString); var store = new WiredRewardStore(database);
            var config = WiredRewards.Defaults() with { Text = $"1,furni#{baseId},100" };
            var loadedOwner = box.OwnerId;
            box.OwnerId = userId + 1;
            Assert.Equal(8, store.ClaimAndGrant(box, roomId, f.Habbo, config, definitions, 100).Reason);
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_reward_state WHERE item_id=@boxId", new { boxId }));
            box.OwnerId = loadedOwner;
            var grant = store.ClaimAndGrant(box, roomId, f.Habbo, config, definitions, 100);
            Assert.Equal(5, grant.Reason); Assert.Equal(0, sent); Assert.Empty(f.Habbo.Inventory.Furniture.GetItems);
            Assert.Equal(1, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id=@id AND user_id=@user AND room_id=0", new { id = grant.Furniture!.Id, user = userId }));
            WiredRewards.Publish(f.Habbo, grant); Assert.True(sent > 0); Assert.Single(f.Habbo.Inventory.Furniture.GetItems);
            Assert.Equal(2, new WiredRewardStore(database).ClaimAndGrant(box, roomId, f.Habbo, config, definitions, 200).Reason);
            config = config with { IntParams = [3, 0, 0, 1, 0] };
            Assert.Equal(2, store.ClaimAndGrant(box, roomId, f.Habbo, config, definitions, 159).Reason);
            Assert.Equal(5, store.ClaimAndGrant(box, roomId, f.Habbo, config, definitions, 160).Reason);
            admin.Execute("DELETE FROM wired_reward_state WHERE item_id=@boxId", new { boxId });
            config = config with { IntParams = [3, 0, 1, 1, 0] };
            var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() => new WiredRewardStore(database).ClaimAndGrant(box, roomId, f.Habbo, config, definitions, 1000)));
            var results = await Task.WhenAll(tasks); Assert.Single(results, result => result.Reason == 5); Assert.Equal(3, results.Count(result => result.Reason == 1));
            admin.Execute("DELETE FROM wired_reward_state WHERE item_id=@boxId", new { boxId });
            var unique = config with { IntParams = [3, 1, 0, 1, 0], Text = $"1,furni#{baseId},100;1,probe_product,100" };
            Assert.Equal(5, store.ClaimAndGrant(box, roomId, f.Habbo, unique, definitions, 1000).Reason);
            Assert.Equal(5, store.ClaimAndGrant(box, roomId, f.Habbo, unique, definitions, 1060).Reason);
            Assert.Equal(2, store.ClaimAndGrant(box, roomId, f.Habbo, unique, definitions, 1120).Reason);
            var state = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, WiredRewardClaim>>(admin.QuerySingle<string>("SELECT claims FROM wired_reward_state WHERE item_id=@boxId", new { boxId }))!;
            Assert.Equal(2, state[f.Habbo.Id].Count); Assert.Contains("probe_product", state[f.Habbo.Id].ReceivedCodes); Assert.Contains($"furni#{baseId}", state[f.Habbo.Id].ReceivedCodes);
            unique = unique with { Text = "1,new_product_after_edit,100" };
            Assert.Equal(5, store.ClaimAndGrant(box, roomId, f.Habbo, unique, definitions, 1120).Reason); // Existing claims survive edits; new codes become available.
            admin.Execute("DELETE FROM wired_reward_state WHERE item_id=@boxId", new { boxId });
            var before = admin.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE user_id=@user", new { user = userId });
            database.FailSqlPrefix = "UPDATE wired_reward_state";
            Assert.Throws<ModernWiredDatabaseProbe.InjectedCommandFailure>(() => store.ClaimAndGrant(box, roomId, f.Habbo, config, definitions, 2000));
            Assert.Equal(before, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE user_id=@user", new { user = userId }));
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_reward_state WHERE item_id=@boxId", new { boxId }));
            database.FailSqlPrefix = null;
            admin.Execute("INSERT INTO badge_definitions(code,required_right) VALUES (@badgeCode,'')", new { badgeCode });
            var badgeConfig = WiredRewards.Defaults() with { Text = "0," + badgeCode + ",100" };
            database.FailSqlPrefix = "UPDATE wired_reward_state";
            Assert.Throws<ModernWiredDatabaseProbe.InjectedCommandFailure>(() => store.ClaimAndGrant(box, roomId, f.Habbo, badgeConfig, definitions, 2000));
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM user_badges WHERE user_id=@userId", new { userId }));
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_reward_state WHERE item_id=@boxId", new { boxId }));
            database.FailSqlPrefix = null;
            var badgeGrant = store.ClaimAndGrant(box, roomId, f.Habbo, badgeConfig, definitions, 2000); Assert.Equal(4, badgeGrant.Reason);
            Assert.False(f.Habbo.Inventory.Badges.HasBadge(badgeCode)); WiredRewards.Publish(f.Habbo, badgeGrant); Assert.True(f.Habbo.Inventory.Badges.HasBadge(badgeCode));
            admin.Execute("DELETE FROM wired_reward_state WHERE item_id=@boxId", new { boxId });
            Assert.Equal(2, store.ClaimAndGrant(box, roomId, f.Habbo, badgeConfig, definitions, 2100).Reason);
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_reward_state WHERE item_id=@boxId", new { boxId }));
        }
        finally
        {
            admin.Execute("DELETE FROM wired_reward_state WHERE item_id=@boxId", new { boxId });
            admin.Execute("DELETE FROM user_badges WHERE user_id=@userId", new { userId });
            admin.Execute("DELETE FROM badge_definitions WHERE code=@badgeCode", new { badgeCode });
            admin.Execute("DELETE FROM items WHERE user_id=@userId", new { userId });
            admin.Execute("DELETE FROM rooms WHERE id=@roomId", new { roomId });
            admin.Execute("DELETE FROM users WHERE id=@userId", new { userId });
        }
    }

    [WiredVariableDatabaseFact]
    public void ActualSnapshotSpawnGivesTwoEphemeralVariablesAndDetachesWithoutDurableValues()
    {
        using var f = new TeleportFixture();
        var connectionString = ModernWiredDatabaseProbe.GuardedConnectionString(); using var admin = new MySqlConnection(connectionString); admin.Open();
        var userId = 0u; var roomId = 0u; var variableId = 0u; var operandId = 0u;
        var dbField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!; var original = dbField.GetValue(null);
        try
        {
            var suffix = "WT" + Guid.NewGuid().ToString("N")[..10];
            userId = ModernWiredDatabaseProbe.Insert(admin, "users", new() { ["username"] = suffix, ["password"] = "unused", ["mail"] = suffix + "@invalid" });
            roomId = ModernWiredDatabaseProbe.Insert(admin, "rooms", new() { ["owner"] = userId.ToString(), ["caption"] = "Disposable temporary variable probe", ["model_name"] = admin.QueryFirst<string>("SELECT id FROM room_models LIMIT 1") });
            var baseId = admin.QueryFirst<uint>("SELECT id FROM furniture WHERE type='s' LIMIT 1");
            variableId = ModernWiredDatabaseProbe.Insert(admin, "items", new() { ["user_id"] = userId, ["room_id"] = roomId, ["base_item"] = baseId, ["extra_data"] = "", ["wall_pos"] = "" });
            admin.Execute("INSERT INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES (@variableId,'wf_var_furni',1,@config)", new { variableId, config = System.Text.Json.JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, 1], Text = "spawnvalue" }) });
            f.Room.Id = roomId; f.Room.OwnerId = (int)userId;
            f.Target.Definition.Stackable = true;
            var definition = MakeItem(baseId, "probe").Definition; definition.Id = baseId; definition.Stackable = true;
            var definitions = DispatchProxy.Create<IItemDataManager, RecordingProxy>(); ((RecordingProxy)(object)definitions).InvokeMethod = (method, _) => method.Name == "get_Items" ? new Dictionary<uint, ItemDefinition> { [baseId] = definition } : null; f.DefinitionManager = definitions;
            dbField.SetValue(null, new ModernWiredDatabaseProbe.ProbeDatabase(connectionString));
            var spawnId = ModernWiredDatabaseProbe.Insert(admin, "items", new() { ["user_id"] = userId, ["room_id"] = roomId, ["base_item"] = baseId, ["extra_data"] = "", ["wall_pos"] = "" });
            var loadedRows = new DataTable();
            using (var reader = admin.ExecuteReader("SELECT items.*,users.username FROM items JOIN users ON users.id=items.user_id WHERE items.id=@spawnId", new { spawnId }))
                loadedRows.Load(reader);
            var action = ActionBox(f.Room, "wf_act_place_furni");
            action.Item = ItemLoader.ReadRoomItem(Assert.Single(loadedRows.Rows.Cast<DataRow>()), roomId, action.Item.Definition);
            f.Items[spawnId] = action.Item;
            var removals = new List<byte[]>();
            f.Habbo.Client.SendCallback = args =>
            {
                var packet = args.MemoryBuffer.ToArray();
                if (FlashGameClient.DecodeInt16(packet.AsMemory(4, 2)) == ServerPacketHeader.ObjectRemoveComposer) removals.Add(packet);
                return true;
            };
            Assert.True(action.TryValidateConfiguration(new() { IntParams = [(int)baseId, 1, 1, 1, 2, 0] }, out var raw, out _));
            action.ApplyConfiguration(raw);
            Assert.True(action.Execute(Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User])));
            var literal = Assert.Single(f.Room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary));
            Assert.Equal(userId, literal.OwnerId); Assert.Equal((int)userId, literal.UserId);
            Assert.True(f.Room.GetRoomItemHandler().RemoveTemporaryFloorItem(literal));
            var removal = new FlashIncomingPacket { Buffer = Assert.Single(removals).AsMemory(6) };
            Assert.Equal(unchecked((int)literal.Id).ToString(), removal.ReadString()); Assert.False(removal.ReadBool());
            Assert.Equal((int)userId, removal.ReadInt()); Assert.Equal(0, removal.ReadInt()); Assert.False(removal.HasDataRemaining());
            var config = WiredTemporaryFurnitureActions.Defaults("wf_act_place_furni") with {
                TemporaryPlacement = new(Altitude: WiredPlaceAltitudeType.SourceAltitude, SpawnWithVariable: true, Value: 37),
                VariableIds = [$"custom:{variableId}"], Snapshots = [new(0, baseId, 1, 1, 0, 0, "1"), new(0, baseId, 2, 1, 0, 0, "0")]
            };
            Assert.True(action.TryValidateConfiguration(config, out config, out _)); action.ApplyConfiguration(config);
            var context = Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User]); context.VariableFrame = new(roomId, []);
            Assert.True(action.Execute(context));
            var copies = f.Room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary).ToArray(); Assert.Equal(2, copies.Length);
            Assert.All(copies, item => { Assert.Equal(userId, item.OwnerId); Assert.Equal((int)userId, item.UserId); });
            var holders = copies.Select(WiredVariableRuntimeFrames.FurniHolder).ToArray(); Assert.NotEqual(holders[0].StorageId, holders[1].StorageId);
            var frame = new WiredVariableFrame(roomId, holders); var module = f.Room.GetWired().Variables.Module; var reference = new WiredVariableReference(WiredVariableTarget.Furni, $"custom:{variableId}");
            Assert.All(holders, holder => Assert.Equal(37, module.Read(reference, holder, frame)!.Value));
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_variable_values WHERE definition_id=@variableId", new { variableId }));
            Assert.True(f.Room.GetRoomItemHandler().RemoveTemporaryFloorItem(copies[0]));
            Assert.Null(module.Read(reference, holders[0], frame)); Assert.Equal(37, module.Read(reference, holders[1], frame)!.Value);
            Assert.True(f.Room.GetRoomItemHandler().RemoveTemporaryFloorItem(copies[1])); Assert.Empty(module.GetStoredHolders(variableId));
            operandId = ModernWiredDatabaseProbe.Insert(admin, "items", new() { ["user_id"] = userId, ["room_id"] = roomId, ["base_item"] = baseId, ["extra_data"] = "", ["wall_pos"] = "" });
            admin.Execute("INSERT INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES (@operandId,'wf_var_context',1,@config)", new { operandId, config = System.Text.Json.JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1], Text = "operand" }) });
            var operandFrame = new WiredVariableFrame(roomId, []);
            Assert.True(module.Mutate(new(WiredVariableTarget.Context, $"custom:{operandId}"), new(WiredVariableTarget.Context, 0, 0), WiredVariableMutation.Give, 42, operandFrame));
            config = config with { TemporaryPlacement = config.TemporaryPlacement! with { ValueIsVariable = true, ValueTarget = 2 }, VariableIds = [$"custom:{variableId}", $"custom:{operandId}"] };
            Assert.True(action.TryValidateConfiguration(config, out config, out _)); action.ApplyConfiguration(config);
            context = Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User]); context.VariableFrame = operandFrame;
            Assert.True(action.Execute(context)); copies = f.Room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary).ToArray();
            holders = copies.Select(WiredVariableRuntimeFrames.FurniHolder).ToArray(); frame = new(roomId, holders);
            Assert.All(holders, holder => Assert.Equal(42, module.Read(reference, holder, frame)!.Value));
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_variable_values WHERE definition_id=@variableId", new { variableId }));
            f.Room.GetRoomItemHandler().Dispose(); Assert.Empty(module.GetStoredHolders(variableId));

        }
        finally
        {
            f.Engine.Clear(); dbField.SetValue(null, original);
            admin.Execute("DELETE FROM wired_variable_values WHERE definition_id=@variableId", new { variableId });
            admin.Execute("DELETE FROM wired_item_configurations WHERE item_id IN (@variableId,@operandId)", new { variableId, operandId });
            admin.Execute("DELETE FROM items WHERE user_id=@userId", new { userId });
            admin.Execute("DELETE FROM rooms WHERE id=@roomId", new { roomId }); admin.Execute("DELETE FROM users WHERE id=@userId", new { userId });
        }
    }

    [Fact]
    public void ActualProjectileMoveSamplesPacketClockAndLaunchCollisionsWithoutLateArrivals()
    {
        using var f = new TeleportFixture(); var room = f.Room; var map = room.GetGameMap();
        var definition = MakeItem(5, "projectile").Definition; definition.Stackable = true; definition.Walkable = true;
        var mover = Assert.IsType<Item>(room.GetRoomItemHandler().PlaceTemporaryFloorItem(definition, 1, 0, 1, 0, 0.25));
        f.Target.Definition.Stackable = true; map.AddToMap(f.Target);
        var obstacle = MakeItem(2, "obstacle"); obstacle.Definition.Stackable = true; obstacle.SetState(2, 1, 0, Gamemap.GetAffectedTiles(1, 1, 2, 1, 0)); f.Items[2] = obstacle; map.AddToMap(obstacle);
        var user = Bot(room, 8); user.SetPos(1, 1, 0); RoomUsers(room)[8] = user; map.AddUserToMap(user, new(1, 1));
        var launchUser = Bot(room, 9); launchUser.SetPos(0, 1, 0); RoomUsers(room)[9] = launchUser; map.AddUserToMap(launchUser, new(0, 1));
        var flights = WiredProjectileFlights.For(room); Assert.Null(flights.Read(mover, "@projectile.animation.position.x", 1000));
        var context = Context(room, new(WiredEventKind.Use), f.Items.Values.ToArray(), RoomUsers(room).Values.ToArray()); context.NowMilliseconds = 1000;
        context.Policy.Addons.Projectile = new(new HashSet<uint> { mover.Id }, null, 0, null, Plus.HabboHotel.Items.Wired.Modern.Addons.WiredProjectileDistance.Normal, 0);
        context.Policy.Addons.AnimationTimeMs = 800;
        var movements = new WiredRoomMovement(room.GetWired().DispatchWalkTransition);
        Assert.True(movements.MoveFurniture(context, mover, 2, 1, 0, 2.25));
        Assert.Equal(0, flights.Read(mover, "@projectile.animation.position.x", 999)); Assert.Equal(1, flights.Read(mover, "@projectile.animation.is_traveling", 1000));
        Assert.Equal(0, flights.Read(mover, "@projectile.animation.user_collisions", 1000)); // Launch tile is excluded.
        Assert.True(WiredRoomOperations.RelocateAvatar(room, user, 1, 2, false));
        var late = Bot(room, 10); late.SetPos(2, 1, 0); RoomUsers(room)[10] = late; map.AddUserToMap(late, new(2, 1));
        Assert.Equal(1, flights.Read(mover, "@projectile.animation.tiles_traveled", 1400));
        Assert.Equal(1, flights.Read(mover, "@projectile.animation.position.x", 1400)); Assert.Equal(1, flights.Read(mover, "@projectile.animation.position.y", 1400));
        Assert.Equal(125, flights.Read(mover, "@projectile.animation.position.altitude", 1400));
        Assert.Equal(1, flights.Read(mover, "@projectile.animation.furni_collisions", 1400)); Assert.Equal(1, flights.Read(mover, "@projectile.animation.user_collisions", 1400));
        Assert.Null(flights.Read(mover, "@projectile.animation.is_traveling", 1800));
        Assert.Equal(2, flights.Read(mover, "@projectile.animation.tiles_traveled", 9999)); Assert.Equal(225, flights.Read(mover, "@projectile.animation.position.altitude", 9999));
        Assert.Equal(2, flights.Read(mover, "@projectile.animation.furni_collisions", 9999)); Assert.Equal(1, flights.Read(mover, "@projectile.animation.user_collisions", 9999));
        Assert.False(movements.MoveFurniture(context, mover, 5, 1, 0, null)); Assert.Equal(2, flights.Read(mover, "@projectile.animation.position.x", 9999));
    }

    [Fact]
    public void ProjectileScopeRepeatedFlightInstantMoveAndReusedIdentityHaveRealLifecycle()
    {
        using var f = new TeleportFixture(); var room = f.Room; var definition = MakeItem(5, "projectile").Definition; definition.Stackable = true;
        var mover = Assert.IsType<Item>(room.GetRoomItemHandler().PlaceTemporaryFloorItem(definition, 1, 0, 1, 0));
        var flights = WiredProjectileFlights.For(room); var movement = new WiredRoomMovement(room.GetWired().DispatchWalkTransition);
        var ctx = Context(room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User]); ctx.NowMilliseconds = 1000;
        ctx.Policy.Addons.DisableAnimation = true; ctx.Policy.Addons.Projectile = new(new HashSet<uint> { 999 }, null, 0, null, Plus.HabboHotel.Items.Wired.Modern.Addons.WiredProjectileDistance.Normal, 0);
        Assert.True(movement.MoveFurniture(ctx, mover, 1, 2, 0, null)); Assert.Null(flights.Read(mover, "@projectile.animation.tiles_traveled", 1000));
        ctx.Policy.Addons.Projectile = ctx.Policy.Addons.Projectile with { ItemIds = new HashSet<uint> { mover.Id } };
        Assert.True(movement.MoveFurniture(ctx, mover, 2, 2, 0, null)); Assert.Null(flights.Read(mover, "@projectile.animation.is_traveling", 1000)); Assert.Equal(2, flights.Read(mover, "@projectile.animation.position.x", 1000));
        ctx.NowMilliseconds = 2000; ctx.Policy.Addons.DisableAnimation = false; ctx.Policy.Addons.AnimationTimeMs = 1000;
        Assert.True(movement.MoveFurniture(ctx, mover, 0, 2, 0, null)); Assert.Equal(2, flights.Read(mover, "@projectile.animation.position.x", 2000)); Assert.Equal(1, flights.Read(mover, "@projectile.animation.position.x", 2500));
        Assert.Equal(1, flights.Read(mover, "@projectile.animation.is_traveling", 2500));
        var sameIdDifferentItem = MakeItem(mover.Id, "replacement"); f.Items[mover.Id] = sameIdDifferentItem;
        Assert.Null(flights.Read(sameIdDifferentItem, "@projectile.animation.position.x", 2500)); Assert.Null(flights.Read(mover, "@projectile.animation.position.x", 2500));
        f.Items[mover.Id] = mover; Assert.True(flights.Begin(mover, 2, 2, 0, 1000, 3000)); flights.Forget(sameIdDifferentItem); Assert.NotNull(flights.Read(mover, "@projectile.animation.position.x", 3000));
        flights.Forget(mover); Assert.Null(flights.Read(mover, "@projectile.animation.position.x", 3000));
        Assert.True(flights.Begin(mover, 2, 2, 0, 1000, 4000)); flights.Clear(); Assert.Null(flights.Read(mover, "@projectile.animation.position.x", 4000));
        Assert.True(flights.Begin(mover, 2, 2, 0, 1000, 5000)); Assert.True(room.GetRoomItemHandler().RemoveTemporaryFloorItem(mover)); Assert.Null(flights.Read(mover, "@projectile.animation.position.x", 5000));
    }

    public class RecordingProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> InvokeMethod = (_, _) => null;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!, args);
    }
    private static WiredBoxDescriptor Descriptor(string name) { Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor)); return descriptor; }
    private static Item MakeItem(uint id, string name) => new()
    {
        Id = id, ExtraData = new LegacyDataFormat { Data = "0" }, Definition = new() { Type = ItemType.Floor, ItemName = name, InteractionName = name,
            Width = 1, Length = 1, Modes = 2, AdjustableHeights = [], VendingIds = [], PublicName = name }
    };
    private static (Room Room, Gamemap Map, ConcurrentDictionary<uint, Item> Items) World()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var map = new Gamemap(room, new RoomModel("wired-test", 0, 0, 0, 0, "000\r000\r000", 0, 0, true), TestLogging.Navigation);
        var handler = new RoomItemHandling(room);
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, map);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, handler);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room));
        typeof(Gamemap).GetProperty("GameMap")!.SetValue(map, new byte[3, 3]);
        typeof(Gamemap).GetProperty("EffectMap")!.SetValue(map, new byte[3, 3]);
        return (room, map, (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!);
    }
    private static WiredRuntimeContext Context(Room room, WiredRuntimeEvent @event, Item[] items, RoomUser[] users) =>
        new(room, @event, new(() => items, () => users), new UnusedOperations());
    private sealed class UnusedOperations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }
}
