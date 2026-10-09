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
        Assert.True(box.TryValidateConfiguration(proposed, out var configuration, out var error), error);
        box.ApplyConfiguration(configuration);
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
