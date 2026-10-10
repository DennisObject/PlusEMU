using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Rooms.Avatar;
using Plus.Communication.Packets.Incoming.Rooms.Chat;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Xunit;

namespace Plus.Tests;

public class WiredAvatarPacketHookTests
{
    [Fact]
    public async Task MovementAndHandHandlersDecodeAllFieldsBeforeDelegating()
    {
        var service = new RecordingAvatarActions();
        var world = new World(1);
        var movement = Packet(3, 4);
        await new Plus.Communication.Packets.Incoming.Rooms.Engine.MoveAvatarEvent(service)
            .Parse(world.Client, movement);
        Assert.Equal((3, 4), service.MoveTarget);
        Assert.False(movement.HasDataRemaining());
        var hand = Packet(17);
        await new Plus.Communication.Packets.Incoming.Rooms.Action.GiveHandItemEvent(service)
            .Parse(world.Room, world.Client, hand);
        Assert.Equal(17, service.HandTarget);
        Assert.False(hand.HasDataRemaining());
        Assert.Empty(world.SentPackets);
    }

    [Fact]
    public void WaveAndChangedDanceProgressTheInjectedRewardManager()
    {
        var world = new World(1);
        world.Actions.PerformAction(world.Room, world.Client, 1);
        world.Actions.PerformAction(world.Room, world.Client, 2);
        world.Actions.Dance(world.Room, world.Client, 1);
        world.Actions.Dance(world.Room, world.Client, 1);
        world.Actions.Dance(world.Room, world.Client, 0);
        Assert.Equal(new[] { RewardTrackActions.Wave, RewardTrackActions.Dance }, world.Rewards.Progresses);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(7, 11)]
    public async Task ExpressionPacketRunsActualConfiguredTriggerOnceWithActorIdentity(int expression, int editorAction)
    {
        var world = new World(editorAction);
        await new ActionEvent(world.Actions).Parse(world.Room, world.Client, Packet(expression));
        world.FlushWired();
        var observed = Assert.Single(world.Capture.Events);
        Assert.Same(world.Actor, observed.Actor);
        Assert.Equal(editorAction, observed.Action);
        Assert.Equal(-1, observed.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task SignPacketRunsExactSignFilterAndRejectsOutOfRangeValue(int sign)
    {
        var world = new World(9, sign);
        world.Actor.IdleTime = 10;
        await new ApplySignEvent(world.Actions).Parse(world.Room, world.Client, Packet(sign));
        world.FlushWired();
        var observed = Assert.Single(world.Capture.Events);
        Assert.Same(world.Actor, observed.Actor);
        Assert.Equal(sign, observed.Code);
        Assert.Equal(sign.ToString(), world.Actor.Statusses["sign"]);
        Assert.Equal((int)WiredAvatarAction.Sign, observed.Action);
        Assert.Equal(0, world.Actor.IdleTime);
        Assert.Equal(sign.ToString(), world.Capture.SignStatusAtDispatch);
        await new ApplySignEvent(world.Actions).Parse(world.Room, world.Client, Packet(18));
        Assert.Single(world.Capture.Events);
        Assert.Equal(sign.ToString(), world.Actor.Statusses["sign"]);
    }

    [Fact]
    public void SignDeadlineExpiresBeforeExactAndAfterBoundariesAndRefreshes()
    {
        var clock = new ManualClock(new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var world = new World(9, 4, clock);
        world.Actions.ApplySign(world.Room, world.Client, 4);
        var firstDeadline = clock.GetUtcNow().AddSeconds(5);
        Assert.Equal(firstDeadline, world.Actor.SignExpiresAt);

        world.Actor.UpdateNeeded = false;
        clock.Now = firstDeadline.AddTicks(-1);
        world.Users.UpdateSignStatus(world.Actor);
        Assert.Equal("4", world.Actor.Statusses["sign"]);
        Assert.Equal(firstDeadline, world.Actor.SignExpiresAt);
        Assert.False(world.Actor.UpdateNeeded);

        clock.Now = firstDeadline;
        world.Users.UpdateSignStatus(world.Actor);
        Assert.False(world.Actor.Statusses.ContainsKey("sign"));
        Assert.Null(world.Actor.SignExpiresAt);
        Assert.True(world.Actor.UpdateNeeded);

        world.Actor.UpdateNeeded = false;
        clock.Now = firstDeadline.AddHours(1);
        world.Users.UpdateSignStatus(world.Actor);
        Assert.False(world.Actor.UpdateNeeded);

        world.Actions.ApplySign(world.Room, world.Client, 5);
        clock.Now = clock.Now.AddSeconds(4);
        var supersededDeadline = clock.Now.AddSeconds(1);
        world.Actions.ApplySign(world.Room, world.Client, 6);
        var refreshedDeadline = clock.Now.AddSeconds(5);
        Assert.Equal(refreshedDeadline, world.Actor.SignExpiresAt);
        clock.Now = supersededDeadline;
        world.Users.UpdateSignStatus(world.Actor);
        Assert.Equal("6", world.Actor.Statusses["sign"]);
        Assert.Equal(refreshedDeadline, world.Actor.SignExpiresAt);
        clock.Now = refreshedDeadline.AddTicks(1);
        world.Users.UpdateSignStatus(world.Actor);
        Assert.False(world.Actor.Statusses.ContainsKey("sign"));
        Assert.Null(world.Actor.SignExpiresAt);
    }

    [Fact]
    public void ApplySignRejectsInvalidMissingActorAndForeignRoom()
    {
        var world = new World(9, 3);
        world.Actions.ApplySign(world.Room, world.Client, -1);
        Assert.Empty(world.Capture.Events);

        world.RemoveActor();
        world.Actions.ApplySign(world.Room, world.Client, 3);
        Assert.Empty(world.Capture.Events);

        world.AddActor();
        world.Client.GetHabbo().CurrentRoom = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        world.Actions.ApplySign(world.Room, world.Client, 3);
        Assert.Empty(world.Capture.Events);
        Assert.False(world.Actor.Statusses.ContainsKey("sign"));
    }

    [Fact]
    public async Task SignHandlerOnlyDecodesAndDelegates()
    {
        var actions = new RecordingAvatarActions();
        await new ApplySignEvent(actions).Parse(null!, null!, Packet(12));
        Assert.Equal(12, actions.SignId);
        Assert.Null(actions.Room);
        Assert.Null(actions.Session);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task DancePacketCarriesActualDanceCodeAndStopDoesNotFire(int dance)
    {
        var world = new World(10, dance);
        world.Client.GetHabbo().Access = Plus.HabboHotel.Permissions.UserAccess.Create([], [new(Plus.HabboHotel.Permissions.PermissionKeys.ClubAccess, false)]);
        var handler = new DanceEvent(world.Actions);
        await handler.Parse(world.Room, world.Client, Packet(dance));
        world.FlushWired();
        var observed = Assert.Single(world.Capture.Events);
        Assert.Same(world.Actor, observed.Actor);
        Assert.Equal(dance, observed.Code);
        await handler.Parse(world.Room, world.Client, Packet(0));
        Assert.Single(world.Capture.Events);
        Assert.Equal(0, world.Actor.DanceId);
    }

    [Fact]
    public async Task CurrentPostureRequestsFireOnlyActualSitAndStandTransitions()
    {
        var world = new World(6);
        world.AddTrigger(7);
        var handler = new SitEvent(world.Actions);
        await handler.Parse(world.Client, Packet(1));
        await handler.Parse(world.Client, Packet(1));
        Assert.True(world.Actor.IsSitting);
        world.FlushWired();
        Assert.Single(world.Capture.Events);
        await handler.Parse(world.Client, Packet(0));
        await handler.Parse(world.Client, Packet(0));
        Assert.False(world.Actor.IsSitting);
        Assert.Equal(0, world.Actor.Z);
        world.FlushWired();
        Assert.Equal(new[] { 6, 7 }, world.Capture.Events.Select(evt => evt.Action));
        Assert.All(world.Capture.Events, evt => Assert.Same(world.Actor, evt.Actor));
    }

    [Fact]
    public void PostureAndLookRequestsPreserveDenialsAndStateTransitions()
    {
        var world = new World(6);
        world.Actions.SetPosture(world.Client, 2);
        Assert.False(world.Actor.IsSitting);
        world.Actor.IsWalking = true;
        world.Actions.SetPosture(world.Client, 1);
        Assert.False(world.Actor.IsSitting);
        world.Actor.IsWalking = false;
        world.Actor.RotBody = 3;
        world.Actions.SetPosture(world.Client, 1);
        Assert.True(world.Actor.IsSitting);
        Assert.Equal(2, world.Actor.RotBody);
        Assert.Equal(-0.35, world.Actor.Z);

        world.Actor.UpdateNeeded = false;
        world.Actor.IsAsleep = true;
        world.Actions.LookTo(world.Room, world.Client, 4, 4);
        Assert.False(world.Actor.UpdateNeeded);
        world.Actor.IsAsleep = false;
        world.Actions.LookTo(world.Room, world.Client, 4, 4);
        Assert.True(world.Actor.UpdateNeeded);
    }

    [Fact]
    public void TypingRequestsPreserveRepeatedPublicationAndMissingActorNoOp()
    {
        var world = new World(6);
        world.Actions.SetTyping(world.Client, true);
        world.Actions.SetTyping(world.Client, true);
        world.Actions.SetTyping(world.Client, false);
        Assert.Equal(3, world.SentPackets.Count);

        world.RemoveActor();
        world.Actions.SetTyping(world.Client, true);
        Assert.Equal(3, world.SentPackets.Count);
    }

    [Fact]
    public async Task AvatarHandlersDecodePrimitivesAndDelegateOnly()
    {
        var actions = new RecordingAvatarActions();
        await new ActionEvent(actions).Parse(null!, null!, Packet(7));
        await new DanceEvent(actions).Parse(null!, null!, Packet(4));
        await new SitEvent(actions).Parse(null!, Packet(1));
        await new LookToEvent(actions).Parse(null!, null!, Packet(8, 9));
        await new StartTypingEvent(actions).Parse(null!, Packet());
        await new CancelTypingEvent(actions).Parse(null!, Packet());

        Assert.Equal(7, actions.Action);
        Assert.Equal(4, actions.DanceId);
        Assert.Equal(1, actions.Posture);
        Assert.Equal((8, 9), actions.LookTarget);
        Assert.Equal(new[] { true, false }, actions.Typing);
    }

    [Fact]
    public void DanceAndSleepComposersCaptureVirtualIdAndRecomposeDeterministically()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var actor = new RoomUser(42, 1, 7, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        var dance = new DanceComposer(actor.VirtualId, 4);
        var sleep = new SleepComposer(actor.VirtualId, true);
        actor.VirtualId = 99;

        var danceFirst = new HabbiconTestSupport.RecordingPacket();
        var danceSecond = new HabbiconTestSupport.RecordingPacket();
        dance.Compose(danceFirst);
        dance.Compose(danceSecond);
        Assert.Equal(new object[] { 7, 4 }, danceFirst.Writes);
        Assert.Equal(danceFirst.Writes, danceSecond.Writes);

        var sleepFirst = new HabbiconTestSupport.RecordingPacket();
        var sleepSecond = new HabbiconTestSupport.RecordingPacket();
        sleep.Compose(sleepFirst);
        sleep.Compose(sleepSecond);
        Assert.Equal(new object[] { 7, true }, sleepFirst.Writes);
        Assert.Equal(sleepFirst.Writes, sleepSecond.Writes);
    }

    private static FlashIncomingPacket Packet(params int[] values)
    {
        using var stream = PlusMemoryStream.GetStream();
        var packet = new FlashOutgoingPacket(stream);

        foreach (var value in values) {
            packet.WriteInteger(value);
        }

        return new() { Buffer = stream.ToArray().AsMemory(6) };
    }

    private sealed class World
    {
        public Room Room { get; }
        public FlashGameClient Client { get; }
        public RoomUser Actor { get; }
        public CaptureAction Capture { get; }
        public RoomUserManager Users { get; }
        public IRoomAvatarActionService Actions { get; }
        public RecordingActionRewards Rewards { get; } = new();
        public List<byte[]> SentPackets { get; } = [];
        private readonly WiredComponent _wired;
        private readonly ConcurrentDictionary<int, RoomUser> _users;
        private uint _next = 10;
        public void FlushWired()
        {
            _wired.OnFastCycle();
            _wired.OnFastCycle();
        }

        public World(int action, int code = -1, TimeProvider? clock = null)
        {
            Room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            Room.Id = 1;
            var items = new RoomItemHandling(Room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
            Users = new RoomUserManager(Room, TestRoomUserStore.Instance, clock ?? TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
            Actions = new RoomAvatarActionService(clock ?? TimeProvider.System, new NoQuests(), Rewards);
            Set(Room, "_roomItemHandling", items);
            Set(Room, "_roomUserManager", Users);
            _wired = new WiredComponent(Room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
            Set(Room, "_wiredComponent", _wired);
            Client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
            {
                Revision = new()
                {
                    InternalIdToOutgoingIdMapping = new Dictionary<uint, uint>
                    {
                        [ServerPacketHeader.ActionComposer] = 1,
                        [ServerPacketHeader.DanceComposer] = 2,
                        [ServerPacketHeader.SleepComposer] = 3,
                        [ServerPacketHeader.AvatarEffectComposer] = 4,
                        [ServerPacketHeader.UserTypingComposer] = 5
                    }
                },
                SendCallback = packet =>
                {
                    SentPackets.Add(packet.MemoryBuffer.Span.Slice(packet.Offset, packet.Count).ToArray());

                    return true;
                }
            };
            Client.SetHabbo(new Habbo { Id = 42, Username = "actor", CurrentRoom = Room, Client = Client, Effects = new EffectsComponent(new FixedTimeProvider(FixedTimeProvider.Epoch)) });
            Actor = new RoomUser(42, 1, 7, Room, Client, TestChatEmotions.Unused, TestRewardProgress.Unused);
            _users = (ConcurrentDictionary<int, RoomUser>)Get(Users, "_users");
            AddActor();
            var captureItem = Item("wf_act_toggle_state");
            Capture = new CaptureAction(Room, captureItem);
            Assert.True(_wired.AddBox(Capture));
            AddTrigger(action, code);
        }

        public void AddActor() => _users.TryAdd(Actor.VirtualId, Actor);
        public void RemoveActor() => _users.TryRemove(Actor.VirtualId, out _);

        public void AddTrigger(int action, int code = -1)
        {
            var item = Item("wf_trg_user_performs_action");
            var trigger = _wired.CreateConfiguredBox(item)!;
            var parameters = new[] { action, action == 9 ? 1 : 0, action == 9 ? code : 0, action == 10 ? 1 : 0, action == 10 ? code : 1 };
            Assert.True(trigger.TryValidateConfiguration(new() { IntParams = [.. parameters] }, out var config, out var error), error);
            trigger.ApplyConfiguration(config);
            Assert.True(_wired.AddBox(trigger));
        }

        private Item Item(string name)
        {
            var item = new Item { Id = _next++, ExtraData = new LegacyDataFormat { Data = "1" }, Definition = new() { ItemName = name } };
            ((ConcurrentDictionary<uint, Item>)Get(Room.GetRoomItemHandler(), "_floorItems")).TryAdd(item.Id, item);

            return item;
        }
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingAvatarActions : IRoomAvatarActionService
    {
        public Room? Room { get; private set; }
        public GameClient? Session { get; private set; }
        public int SignId { get; private set; }
        public int Action { get; private set; }
        public int DanceId { get; private set; }
        public int Posture { get; private set; }
        public (int X, int Y) LookTarget { get; private set; }
        public List<bool> Typing { get; } = [];
        public (int X, int Y) MoveTarget;
        public int HandTarget;
        public void Move(GameClient session, int x, int y) => MoveTarget = (x, y);
        public void GiveHandItem(Room room, GameClient session, int userId) => HandTarget = userId;
        public void PerformAction(Room room, GameClient session, int action) => Action = action;
        public void Dance(Room room, GameClient session, int danceId) => DanceId = danceId;
        public void SetPosture(GameClient session, int posture) => Posture = posture;
        public void LookTo(Room room, GameClient session, int x, int y) => LookTarget = (x, y);
        public void SetTyping(GameClient session, bool typing) => Typing.Add(typing);
        public void ApplySign(Room room, GameClient session, int signId)
            => (Room, Session, SignId) = (room, session, signId);
    }

    private sealed class CaptureAction(Room room, Item item) : WiredModernBox(room, item,
        WiredBoxRegistry.All.Single(entry => entry.CanonicalName == "wf_act_toggle_state")), IWiredContextualAction
    {
        public List<WiredRuntimeEvent> Events { get; } = [];
        public string? SignStatusAtDispatch { get; private set; }
        public bool IsNegative => false;
        public override bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
        {
            validated = proposed;
            error = "";

            return true;
        }
        public override bool Execute(WiredRuntimeContext context)
        {
            Events.Add(context.Event);

            if (context.Event.Action == (int)WiredAvatarAction.Sign) {
                SignStatusAtDispatch = context.Event.Actor?.Statusses.GetValueOrDefault("sign");
            }

            return true;
        }
    }

    private sealed class RecordingActionRewards : IRewardTrackManager
    {
        public List<string> Progresses { get; } = [];
        public void Progress(GameClient session, string actionType, int amount = 1) => Progresses.Add(actionType);
        public void SendTracks(GameClient session) => throw new NotSupportedException();
        public Task Claim(GameClient session, string trackId, string prizeId) => throw new NotSupportedException();
        public void PurchasePremium(GameClient session, string trackId) => throw new NotSupportedException();
    }

    private sealed class NoQuests : IQuestManager
    {
        public void ProgressUserQuest(GameClient session, QuestType type, int data = 0) { }
        public void Init() => throw new NotSupportedException();
        public Quest GetQuest(int id) => throw new NotSupportedException();
        public int GetAmountOfQuestsInCategory(string category) => throw new NotSupportedException();
        public Quest GetNextQuestInSeries(string category, int number) => throw new NotSupportedException();
        public void GetList(GameClient session, ClientPacket? message) => throw new NotSupportedException();
        public void QuestReminder(GameClient session, int questId) => throw new NotSupportedException();
    }

    private static object Get(object value, string field) => value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Set(object value, string field, object data) => value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, data);
}
