using System.Globalization;
using System.Reflection;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms.Games;
using Plus.HabboHotel.Rooms.Games.Teams;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(false, 17)]
    [InlineData(true, 23)]
    public void ActualSpeechCapturesChatTypeAndStyleForContextVariables(bool shout, int style)
    {
        var (wired, actor) = PrepareSpeech(new SpeechClock());
        AddSpeechBox(301, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "pulse" }, 0);
        var engine = (WiredStackEngine)typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent)
            .GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wired)!;
        WiredRuntimeEvent? observed = null;
        var original = engine.ObserveEvent;
        engine.ObserveEvent = (evt, now) => { original?.Invoke(evt, now); observed = evt; };
        actor.OnChat(style, "pulse", shout);
        var context = BuiltinContext(observed!);
        var frame = new WiredVariableFrame(_room.Id, []) { RuntimeContext = context };
        Assert.Equal(shout ? 1 : 0, wired.ReadBuiltin(new(WiredVariableTarget.Context, "@event.chat.type"), new(WiredVariableTarget.Context, 0, 0), frame));
        Assert.Equal(style, wired.ReadBuiltin(new(WiredVariableTarget.Context, "@event.chat.style"), new(WiredVariableTarget.Context, 0, 0), frame));
    }

    [Fact]
    public void EventBuiltinsKeepAddressedAntennaAndWideVariableChangesAvailableOnlyForTheirEvents()
    {
        var (wired, _) = PrepareSpeech(new SpeechClock());
        long? Read(WiredRuntimeEvent evt, string key) => wired.ReadBuiltin(new(WiredVariableTarget.Context, key),
            new(WiredVariableTarget.Context, 0, 0), new(_room.Id, []) { RuntimeContext = BuiltinContext(evt) });
        var signal = new WiredRuntimeEvent(WiredEventKind.Signal) { Code = unchecked((int)uint.MaxValue) };
        Assert.Equal(uint.MaxValue, Read(signal, "@event.signal.antenna_id"));
        var change = new WiredVariableChange(_room.Id, new(301, WiredVariableTarget.Context, 0), WiredVariableChangeKind.Updated,
            new(long.MinValue, null, null), new(long.MaxValue, null, null), 0, 0)
        { Origin = 1 };
        var variable = new WiredRuntimeEvent(WiredEventKind.Variable) { VariableChange = change };

        foreach (var (key, expected) in new (string, long)[] { ("box_id", 301), ("change_type", 1),
            ("old_value", long.MinValue), ("new_value", long.MaxValue), ("difference", -1), ("change_origin", 3) }) {
            Assert.Equal(expected, Read(variable, "@event.variable_update." + key));
            Assert.True(RoomWiredBuiltinVariables.HasNumericValue(new(WiredVariableTarget.Context, "@event.variable_update." + key)));
            Assert.Null(Read(signal, "@event.variable_update." + key));
        }

        Assert.Null(Read(variable, "@event.signal.antenna_id"));
        Assert.Null(Read(variable, "@event.chat.type"));
        Assert.Null(Read(variable, "@event.chat.style"));
        Assert.True(RoomWiredBuiltinVariables.HasNumericValue(new(WiredVariableTarget.Context, "@event.habbicon")));
        Assert.Null(Read(variable, "@event.habbicon"));
    }

    [Fact]
    public void GlobalBuiltinsUseLiveTeamStateWiredTimerAndConfiguredCalendar()
    {
        var clock = new SpeechClock { Now = new DateTimeOffset(2026, 10, 9, 23, 30, 47, 123, TimeSpan.Zero) };
        var (wired, actor) = PrepareSpeech(clock);
        var games = new GameManager(_room, clock);
        Set("_gameManager", games);
        actor.Team = Team.Red;
        games.Points[(int)Team.Red] = 12;
        var frame = new WiredVariableFrame(_room.Id, []) { RuntimeContext = BuiltinContext(new(WiredEventKind.Enter)) };
        long? Read(string key) => wired.ReadBuiltin(new(WiredVariableTarget.Global, key), new(WiredVariableTarget.Global, 0, 0), frame);
        Assert.Equal(12, Read("@teams.red.score"));
        Assert.True(wired.WriteBuiltin(new(WiredVariableTarget.Global, "@teams.red.score"), new(WiredVariableTarget.Global, 0, 0), 18, frame));
        Assert.Equal(18, Read("@teams.red.score"));
        Assert.False(wired.WriteBuiltin(new(WiredVariableTarget.Global, "@teams.red.size"), new(WiredVariableTarget.Global, 0, 0), 7, frame));
        Assert.Equal(1, Read("@teams.red.size"));
        Assert.Equal(0, Read("@teams.blue.size"));
        Assert.Equal(0, Read("@group_id"));
        _room.LastTimerResetAt = clock.Now.AddMilliseconds(-1499);
        Assert.Equal(2, Read("@wired_timer"));
        _room.LastTimerResetAt = clock.Now;
        Assert.Equal(0, Read("@wired_timer"));
        Assert.Equal(clock.Now.ToUnixTimeMilliseconds(), Read("@current_time"));
        var local = TimeZoneInfo.ConvertTime(clock.Now, wired.Settings.ExplicitTimeZone ?? TimeZoneInfo.Utc);

        foreach (var (field, expected) in new (string, long)[] { ("milliseconds_of_seconds", local.Millisecond),
            ("seconds_of_minute", local.Second), ("minute_of_hour", local.Minute), ("hour_of_day", local.Hour),
            ("day_of_week", ((int)local.DayOfWeek + 6) % 7 + 1), ("day_of_month", local.Day), ("day_of_year", local.DayOfYear),
            ("week_of_year", ISOWeek.GetWeekOfYear(local.DateTime)), ("month_of_year", local.Month), ("year", local.Year) }) {
            Assert.Equal(expected, Read("@current_time." + field));
            Assert.True(RoomWiredBuiltinVariables.HasNumericValue(new(WiredVariableTarget.Global, "@current_time." + field)));
        }

        Assert.False(RoomWiredBuiltinVariables.HasNumericValue(new(WiredVariableTarget.User, "@is_idle")));
    }

    [Fact]
    public void RoomLinkEntryProvenanceIsDestinationScopedConsumedAndClearedOnRefusedPrepare()
    {
        var (wired, actor) = PrepareSpeech(new SpeechClock());
        var player = _client.GetHabbo();
        player.WiredRoomNetworkDestination = _room.Id;
        player.WiredRoomEntrySourceRoomId = 91;
        player.WiredRoomEntryDestinationRoomId = _room.Id;
        actor.WiredRoomEntry = WiredRoomEntrySnapshot.Capture(_room, player);
        var frame = new WiredVariableFrame(_room.Id, []) { RuntimeContext = BuiltinContext(new(WiredEventKind.Enter) { Actor = actor }) };
        Assert.Equal(91, wired.ReadBuiltin(new(WiredVariableTarget.Context, "@event.link.source_room_id"), new(WiredVariableTarget.Context, 0, 0), frame));
        Assert.Equal(0u, player.WiredRoomEntrySourceRoomId);
        Assert.Equal(0u, player.WiredRoomEntryDestinationRoomId);
        Assert.Equal(default, WiredRoomEntrySnapshot.Capture(_room, player));
        player.WiredRoomNetworkDestination = _room.Id;
        player.WiredRoomEntrySourceRoomId = 91;
        player.WiredRoomEntryDestinationRoomId = _room.Id + 1;
        Assert.Equal(new(WiredRoomEntryMethod.RoomNetwork, 0), WiredRoomEntrySnapshot.Capture(_room, player));

        // Teleport mismatch refuses before loading another room; future joins cannot inherit its source.
        var abandoned = new Plus.HabboHotel.Users.Habbo
        {
            CurrentRoom = (Plus.HabboHotel.Rooms.Room)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plus.HabboHotel.Rooms.Room)),
            IsTeleporting = true,
            TeleportingRoomId = 93,
            WiredRoomEntrySourceRoomId = 91,
            WiredRoomEntryDestinationRoomId = 92,
            WiredRoomNetworkDestination = 92
        };
        var (client, _) = HabbiconTestSupport.Client(abandoned);
        abandoned.Client = client;
        abandoned.PrepareRoom(92, "");
        Assert.Equal(0u, abandoned.WiredRoomEntrySourceRoomId);
        Assert.Equal(0u, abandoned.WiredRoomEntryDestinationRoomId);
        Assert.Equal(0u, abandoned.WiredRoomNetworkDestination);
    }

    [Fact]
    public void PackedBuiltinWritesMoveBothCoordinatesAtomicallyAndPreserveRotationForInvalidOccupation()
    {
        var (wired, actor) = PrepareSpeech(new SpeechClock());
        var item = Furni(71, InteractionType.None, WiredBoxType.None);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, 1, 1, 0, true, false, false));
        var frame = WiredVariableRuntimeFrames.Create(BuiltinContext(new(WiredEventKind.Enter) { Actor = actor }));
        var furni = WiredVariableRuntimeFrames.FurniHolder(item);
        var user = WiredVariableRuntimeFrames.UserHolder(actor);
        Assert.True(wired.WriteBuiltin(new(WiredVariableTarget.Furni, "@position"), furni, (2 << 8) | 1, frame));
        Assert.Equal((2, 1), (item.GetX, item.GetY));
        var intermediate = Furni(72, InteractionType.None, WiredBoxType.None);
        intermediate.Definition.Stackable = false;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, intermediate, 1, 1, 0, true, false, false));
        Assert.True(wired.WriteBuiltin(new(WiredVariableTarget.Furni, "@occupation"), furni, (1 << 16) | (2 << 8) | 4, frame));
        Assert.Equal((1, 2, 4), (item.GetX, item.GetY, item.Rotation));
        Assert.True(wired.WriteBuiltin(new(WiredVariableTarget.Furni, "@occupation"), furni, (2 << 16) | (2 << 8) | 99, frame));
        Assert.Equal((2, 2, 4), (item.GetX, item.GetY, item.Rotation));
        Assert.False(wired.WriteBuiltin(new(WiredVariableTarget.Furni, "@position"), furni, (9 << 8) | 1, frame));
        Assert.Equal((2, 2), (item.GetX, item.GetY));
        Assert.True(wired.WriteBuiltin(new(WiredVariableTarget.User, "@position"), user, (1 << 8) | 2, frame));
        Assert.Equal((1, 2), (actor.X, actor.Y));
    }

    [Theory]
    [InlineData("wf_upcounter1", false)]
    [InlineData("wf_game_upcounter1", true)]
    public void ClockBuiltinsReadLiveStateAndPulseWritesUseTheNativeCounterController(string name, bool gameAware)
    {
        var (wired, actor) = PrepareSpeech(new SpeechClock());
        var item = Furni(81, InteractionType.None, WiredBoxType.None);
        item.Definition.ItemName = name;
        item.Definition.InteractionName = name;
        item.ExtraData = new Plus.HabboHotel.Items.DataFormat.LegacyDataFormat { Data = "0" };
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, 1, 1, 0, true, false, false));
        wired.AttachRoomItem(item);
        var frame = WiredVariableRuntimeFrames.Create(BuiltinContext(new(WiredEventKind.Enter) { Actor = actor }));
        var holder = WiredVariableRuntimeFrames.FurniHolder(item);
        long? Read(string token) => wired.ReadBuiltin(new(WiredVariableTarget.Furni, token), holder, frame);
        Assert.Equal(0, Read("~clock.state"));
        Assert.Equal(0, Read("~clock.pulse_count"));
        Assert.Equal(gameAware ? 1 : (long?)null, Read("~clock.is_game_aware"));
        Assert.False(RoomWiredBuiltinVariables.HasNumericValue(new(WiredVariableTarget.Furni, "~clock.is_game_aware")));
        Assert.True(wired.TryUseCounter(item, 0));
        Assert.Equal(1, Read("~clock.state"));
        Assert.True(wired.TryUseCounter(item, 0));
        Assert.Equal(2, Read("~clock.state"));
        Assert.True(wired.WriteBuiltin(new(WiredVariableTarget.Furni, "~clock.pulse_count"), holder, 3, frame));
        Assert.Equal(3, Read("~clock.pulse_count"));
        Assert.Equal("1", item.LegacyDataString);
        Assert.True(wired.TryUseCounter(item, 2));
        Assert.Equal(0, Read("~clock.state"));
        Assert.Equal(0, Read("~clock.pulse_count"));
        wired.DetachRoomItem(item);
        Assert.Null(Read("~clock.state"));
        Assert.Null(Read("~clock.pulse_count"));
    }

    private WiredRuntimeContext BuiltinContext(WiredRuntimeEvent evt) => new(_room, evt,
        new(() => _room.GetRoomItemHandler().GetWallAndFloor, () => _room.GetRoomUserManager().GetRoomUsers(),
            id => _room.GetRoomItemHandler().GetItem(id), id => _room.GetRoomUserManager().GetRoomUserByVirtualId(id)), _room.GetWired());
}
