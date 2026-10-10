using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(false, 1, false)]
    [InlineData(true, 1, false)]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, true)]
    public void ActualSpeechPublishesImmediateEffectsBeforeEchoAndKeepsPositiveDelaysIndependent(bool ordered, int lowerDelay, bool hide)
    {
        var clock = new SpeechClock();
        var (wired, actor) = PrepareSpeech(clock);
        void Add(uint id, string name, WiredConfiguration configuration, double height) => AddSpeechBox(id, name, configuration, height);
        Add(301, "wf_trg_says_something", new() { IntParams = [1, hide ? 1 : 0, 0], Text = "pulse" }, 0);
        Add(302, "wf_act_show_message", new() { IntParams = [0, 1, 0, -1], Text = "A", Delay = lowerDelay }, 1);
        Add(303, "wf_act_show_message", new() { IntParams = [0, 1, 0, -1], Text = "B" }, 2);

        if (ordered) {
            Add(304, "wf_xtra_exec_in_order", new(), 3);
        }

        _client.Packets.Clear();
        string[] Messages() => _client.Packets.Where(packet => packet.Header == ServerPacketHeader.ChatComposer)
            .Select(packet => { var input = new FlashIncomingPacket { Buffer = packet.Body }; input.ReadInt(); return input.ReadString(); }).ToArray();

        actor.OnChat(0, "pulse", false);
        var immediate = Messages();
        Assert.Equal(hide ? new[] { "pulse" } : [], SpeechMessages(ServerPacketHeader.WhisperComposer));

        if (lowerDelay > 0) {
            Assert.Equal(hide ? new[] { "B" } : ["B", "pulse"], immediate);
            clock.Now = clock.Now.AddMilliseconds(499);
            wired.OnFastCycle();
            Assert.Equal(immediate, Messages());
            clock.Now = clock.Now.AddMilliseconds(1);
            wired.OnFastCycle();
            Assert.Equal(immediate.Append("A"), Messages());
        }
        else {
            Assert.Equal(new[] { "A", "B" }, immediate.Take(2).Order());
            Assert.Equal("pulse", immediate[2]);

            if (ordered) {
                Assert.Equal(new[] { "A", "B", "pulse" }, immediate);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSpeechConditionDoesNotHideEchoEvenWhenNegativeEffectsAreAccepted(bool negativeAction)
    {
        var (wired, actor) = PrepareSpeech(new SpeechClock());
        AddSpeechBox(301, "wf_trg_says_something", new() { IntParams = [1, 1, 0], Text = "pulse" }, 0);
        AddSpeechBox(302, "wf_cnd_user_count_in", new() { IntParams = [10, 20, 0] }, 1);
        AddSpeechBox(303, negativeAction ? "wf_act_neg_log" : "wf_act_show_message",
            negativeAction ? new() { IntParams = [0, 0], Text = "negative" }
                : new() { IntParams = [0, 1, 0, -1], Text = "A" }, 2);
        _client.Packets.Clear();
        actor.OnChat(0, "pulse", false);
        Assert.Equal(new[] { "pulse" }, SpeechMessages(ServerPacketHeader.ChatComposer));
        Assert.Empty(SpeechMessages(ServerPacketHeader.WhisperComposer));
        wired.OnFastCycle();
        Assert.Equal(new[] { "pulse" }, SpeechMessages(ServerPacketHeader.ChatComposer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExhaustedSpeechLimitLeavesNormalEchoAndFailedConditionsSpendQuota(bool failedFirst)
    {
        var clock = new SpeechClock();
        var (wired, actor) = PrepareSpeech(clock);
        AddSpeechBox(301, "wf_trg_says_something", new() { IntParams = [1, 1, 0], Text = "pulse" }, 0);
        var condition = AddSpeechBox(302, "wf_cnd_user_count_in", new() { IntParams = failedFirst ? [10, 20, 0] : [1, 1, 0] }, 1);
        AddSpeechBox(303, "wf_act_show_message", new() { IntParams = [0, 1, 0, -1], Text = "A" }, 2);
        AddSpeechBox(304, "wf_xtra_execution_limit", new() { IntParams = [1, 20] }, 3);
        _client.Packets.Clear();
        actor.OnChat(0, "pulse", false);
        Assert.Equal(new[] { failedFirst ? "pulse" : "A" }, SpeechMessages(ServerPacketHeader.ChatComposer));
        Assert.Equal(failedFirst ? [] : new[] { "pulse" }, SpeechMessages(ServerPacketHeader.WhisperComposer));
        Assert.True(condition.TryValidateConfiguration(new() { IntParams = [1, 1, 0] }, out var passing, out var error), error);
        condition.ApplyConfiguration(passing);
        clock.Now = clock.Now.AddMilliseconds(4400);
        _client.Packets.Clear();
        actor.OnChat(0, "pulse", false);
        Assert.Equal(new[] { "pulse" }, SpeechMessages(ServerPacketHeader.ChatComposer));
        Assert.Empty(SpeechMessages(ServerPacketHeader.WhisperComposer));
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 1)]
    public void ActualSpeechCallRunsTheWholeCalleeBeforeEchoWithoutItsConditionGate(bool negativeCaller, bool calleePasses, int secondDelay)
    {
        var clock = new SpeechClock();
        var (wired, actor) = PrepareSpeech(clock);
        AddSpeechBox(301, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "pulse" }, 0);

        if (negativeCaller) {
            AddSpeechBox(302, "wf_cnd_user_count_in", new() { IntParams = [10, 20, 0] }, 1);
        }

        AddSpeechBox(311, "wf_cnd_user_count_in", new() { IntParams = calleePasses ? [1, 1, 0] : [10, 20, 0] }, 0, 2, 2);
        AddSpeechBox(312, "wf_act_show_message", new() { IntParams = [0, 1, 0, -1], Text = "R" }, 1, 2, 2);
        AddSpeechBox(313, "wf_act_show_message", new() { IntParams = [0, 1, 0, -1], Text = "SECOND", Delay = secondDelay }, 2, 2, 2);
        AddSpeechBox(303, negativeCaller ? "wf_act_neg_call_stacks" : "wf_act_call_stacks",
            new() { IntParams = [100], SelectedItems = [312] }, 2);
        _client.Packets.Clear();
        actor.OnChat(0, "pulse", false);
        var immediate = SpeechMessages(ServerPacketHeader.ChatComposer);

        if (secondDelay == 0) {
            Assert.Equal(new[] { "R", "SECOND" }, immediate.Take(2).Order());
            Assert.Equal("pulse", immediate[2]);
        }
        else {
            Assert.Equal(new[] { "R", "pulse" }, immediate);
            clock.Now = clock.Now.AddMilliseconds(500);
            wired.OnFastCycle();
            Assert.Equal(new[] { "R", "pulse", "SECOND" }, SpeechMessages(ServerPacketHeader.ChatComposer));
        }

        Assert.Empty(SpeechMessages(ServerPacketHeader.WhisperComposer));
    }

    [Fact]
    public void ClockSpeechSetupCannotInstallUnboundReboundOrInvalidPickedDrafts()
    {
        PrepareSpeech(new SpeechClock());
        var timer = Furni(503, Plus.HabboHotel.Items.InteractionType.None, Plus.HabboHotel.Items.Wired.WiredBoxType.None);
        timer.Definition.ItemName = "fball_counter";
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, timer, 1, 0, 0, true, false, false));
        var proposed = new WiredConfiguration { IntParams = [0, 100], SelectedItems = [timer.Id] };
        var box = AddSpeechBox(301, "wf_act_control_clock", proposed, 0);
        Assert.False(box.TryValidateConfiguration(proposed, out _, out _));
        var other = WiredBox(302, "wf_act_control_clock", 2, 1);
        Assert.False(other.TryValidateConfiguration(box.Configuration, out _, out _));
        Assert.Equal(new[] { 0 }, box.Configuration.Origin!.Native!.OwnedIntParams.ToArray());
        Assert.Equal(new[] { 100 }, box.Configuration.Origin.Native.FurniSourceTypes.ToArray());
        var missing = box.Configuration.Origin.Native with { PrimaryItems = [new(999, false)] };
        Assert.True(WiredNativeEditorProjection.TryCompile(box.Item.Id, box.Descriptor, missing, out var missingPick));
        Assert.False(box.TryValidateConfiguration(missingPick, out _, out _));
        var wrongKind = box.Configuration.Origin.Native with { PrimaryItems = [new(timer.Id, true)] };
        Assert.False(WiredNativeEditorProjection.TryCompile(box.Item.Id, box.Descriptor, wrongKind, out _));
        Assert.False(WiredNativeEditorProjection.TryCompile(box.Item.Id, box.Descriptor,
            box.Configuration.Origin.Native with { OwnedIntParams = [5] }, out _));
    }

    private (WiredComponent Wired, RoomUser Actor) PrepareSpeech(TimeProvider clock)
    {
        var wired = new WiredComponent(_room, TestLogging.Logger, clock, TestRoomSettings.Empty,
            TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, _database,
            TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty,
            TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused,
            TestWiredAccess.Unused, TestItemRuntime.Travel);
        Set("_wiredComponent", wired);
        var engine = typeof(WiredComponent).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wired)!;
        engine.GetType().GetField("_now", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(engine, new Func<long>(() => clock.GetUtcNow().ToUnixTimeMilliseconds()));
        var users = new RoomUserManager(_room, TestRoomUserStore.Instance, clock, TestRewardProgress.Unused,
            new TestChatEmotions(), TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
        Set("_roomUserManager", users);
        _room.WordFilterList = [];
        _client.GetHabbo().IgnoresComponent = new([]);
        Assert.True(users.AddAvatarToRoom(_client));

        return (wired, Assert.IsType<RoomUser>(users.GetRoomUserByHabbo(7)));
    }


    private IWiredConfiguredItem AddSpeechBox(uint id, string name, WiredConfiguration proposed, double height, int x = 1, int y = 1)
    {
        var box = WiredBox(id, name, x, y);
        box.Item.GetZ = height;

        if (name == "wf_act_control_clock") {
            var native = new WiredNativeEditorConfiguration
            {
                Category = box.Descriptor.Category,
                NativeCode = WiredNativeEditorProjection.Code(name),
                OwnedIntParams = [proposed.IntParams[0]],
                FurniSourceTypes = [proposed.IntParams[1]],
                PrimaryItems = [.. proposed.SelectedItems.Select(id =>
                {
                    var picked = Assert.IsType<Plus.HabboHotel.Items.Item>(_room.GetRoomItemHandler().GetItem(id));

                    return new WiredNativeItemReference(id, picked.IsWallItem);
                })],
                Delay = proposed.Delay
            };
            Assert.Same(_room, box.Item.GetRoom());
            Assert.Same(box.Item, _room.GetRoomItemHandler().GetItem(id));
            Assert.True(WiredNativeEditorProjection.TryCompile(id, box.Descriptor, native, out proposed));
        }

        if (name == "wf_trg_says_something") {
            Assert.Equal(3, proposed.IntParams.Length);
            var native = new WiredNativeEditorConfiguration
            {
                Category = box.Descriptor.Category,
                NativeCode = 0,
                OwnedIntParams = [proposed.IntParams[2], proposed.IntParams[0], proposed.IntParams[1]],
                Text = proposed.Text,
                PrimaryItems = [.. proposed.SelectedItems.Select(pickId =>
                {
                    var picked = Assert.IsType<Plus.HabboHotel.Items.Item>(_room.GetRoomItemHandler().GetItem(pickId));

                    return new WiredNativeItemReference(pickId, picked.IsWallItem);
                })]
            };
            Assert.Equal(0, proposed.Delay);
            Assert.Same(_room, box.Item.GetRoom());
            Assert.Same(box.Item, _room.GetRoomItemHandler().GetItem(id));
            Assert.True(WiredNativeEditorProjection.TryCompile(id, box.Descriptor, native, out proposed));
        }

        if (name == "wf_act_show_message") {
            ModernWiredRuntimeTests.LoadStoredRuntime(Assert.IsType<Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernAction>(box), name, proposed);
        }
        else {
            Assert.True(box.TryValidateConfiguration(proposed, out var configuration, out var error), error);
            box.ApplyConfiguration(configuration);
        }

        Assert.True(_room.GetWired().AddBox(box));

        return box;
    }

    private string[] SpeechMessages(uint header) => _client.Packets.Where(packet => packet.Header == header)
        .Select(packet => { var input = new FlashIncomingPacket { Buffer = packet.Body }; input.ReadInt(); return input.ReadString(); }).ToArray();

    private sealed class SpeechClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1000);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
