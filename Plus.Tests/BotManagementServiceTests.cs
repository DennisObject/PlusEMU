using System.Collections.Concurrent;
using System.Buffers.Binary;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms.AI.Bots;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.AI.Bots;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Speech;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class BotManagementServiceTests
{
    [Fact]
    public void OwnerActionPersistsBeforePublishingRuntimeState()
    {
        var (service, store, bot, client) = Fixture(ownerId: 7, actorId: 7);

        service.SaveAction(client, new(bot.Id, BotAction.Relax, ""));

        Assert.Equal((bot.Id, "freeroam"), Assert.Single(store.WalkingModes));
        Assert.Equal("freeroam", bot.WalkingMode);
    }

    [Fact]
    public void NonOwnerWithoutOverrideCannotMutateBot()
    {
        var (service, store, bot, client) = Fixture(ownerId: 7, actorId: 8);

        service.SaveAction(client, new(bot.Id, BotAction.Relax, ""));

        Assert.Empty(store.WalkingModes);
        Assert.Equal("stand", bot.WalkingMode);
    }

    [Fact]
    public void PersistenceFailureLeavesRuntimeStateUnchanged()
    {
        var (service, store, bot, client) = Fixture(ownerId: 7, actorId: 7);
        store.Fail = true;

        Assert.Throws<InvalidOperationException>(() => service.SaveAction(client, new(bot.Id, BotAction.Relax, "")));
        Assert.Equal("stand", bot.WalkingMode);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("hello;#;invalid;#;7;#;false")]
    [InlineData("hello;#;true;#;invalid;#;false")]
    [InlineData("hello;#;true;#;7;#;false;#;extra")]
    public void MalformedSpeechIsRejectedWithoutPersistenceOrRuntimeMutation(string payload)
    {
        var (service, store, bot, client) = Fixture(ownerId: 7, actorId: 7);
        bot.AutomaticChat = false;
        bot.SpeakingInterval = 11;
        bot.MixSentences = true;

        service.SaveAction(client, new(bot.Id, BotAction.Speech, payload));

        Assert.Equal(0, store.SpeechWrites);
        Assert.False(bot.AutomaticChat);
        Assert.Equal(11, bot.SpeakingInterval);
        Assert.True(bot.MixSentences);
    }

    [Fact]
    public void ValidSpeechPreservesMinimumIntervalAndPublishesAfterPersistence()
    {
        var (service, store, bot, client) = Fixture(ownerId: 7, actorId: 7);

        service.SaveAction(client, new(bot.Id, BotAction.Speech, "hello\r\nworld;#;true;#;3;#;false"));

        Assert.Equal((bot.Id, (uint)42, true, 7, false), store.LastSpeech);
        Assert.Equal(["hello", "world"], bot.RandomSpeech.Select(speech => speech.Message));
        Assert.True(bot.AutomaticChat);
        Assert.Equal(7, bot.SpeakingInterval);
        Assert.False(bot.MixSentences);
    }

    [Fact]
    public async Task OpenHandlerDecodesAndActualServiceCapturesBotEditorData()
    {
        var (service, _, bot, client) = Fixture(ownerId: 7, actorId: 7);
        bot.RandomSpeech.Add(new("hello", bot.Id));
        bot.RandomSpeech.Add(new("world", bot.Id));
        var sent = new List<byte[]>();
        client.SendCallback = args =>
        {
            sent.Add(args.MemoryBuffer.Span.Slice(args.Offset, args.Count).ToArray());

            return true;
        };
        var handler = new OpenBotActionEvent(service);
        await handler.Parse(client, HabbiconTestSupport.Incoming(bot.Id, 2));
        await handler.Parse(client, HabbiconTestSupport.Incoming(bot.Id, 5));
        await handler.Parse(client, HabbiconTestSupport.Incoming(bot.Id, 4));
        Assert.Equal(2, sent.Count);
        var speech = new FlashIncomingPacket { Buffer = sent[0][6..] };
        Assert.Equal(ServerPacketHeader.OpenBotActionComposer, (uint)BinaryPrimitives.ReadUInt16BigEndian(sent[0].AsSpan(4, 2)));
        Assert.Equal(bot.Id, speech.ReadInt());
        Assert.Equal(2, speech.ReadInt());
        Assert.Equal("hello\nworld\n;#;False;#;7;#;False", speech.ReadString());
        var name = new FlashIncomingPacket { Buffer = sent[1][6..] };
        Assert.Equal(bot.Id, name.ReadInt());
        Assert.Equal(5, name.ReadInt());
        Assert.Equal("Helper", name.ReadString());
        client.GetHabbo().CurrentRoom = null;
        await handler.Parse(client, HabbiconTestSupport.Incoming(bot.Id, 5));
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void BotEditorComposerReadsOnlyCapturedScalars()
    {
        var (service, _, bot, client) = Fixture(ownerId: 7, actorId: 7);
        var composer = new OpenBotActionComposer(new(bot.Id, 5, bot.Name));
        bot.Id = 99;
        bot.Name = "changed";

        for (var i = 0; i < 2; i++) {
            var packet = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(packet);
            Assert.Equal(new object[] { 31, 5, "Helper" }, packet.Writes);
        }
    }

    private static (BotManagementService Service, RecordingStore Store, RoomBot Bot, HabboHotel.GameClients.GameClient Client) Fixture(int ownerId, int actorId)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 42;
        var users = new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, users);

        List<RandomSpeech> speech = [];
        var bot = new RoomBot(31, room.Id, "generic", "stand", "Helper", "", "hr-100", 0, 0, 0, 0, 0, 0, 0, 0,
            ref speech, "M", 0, ownerId, false, 7, false, 0);
        var botUser = new RoomUser(0, room.Id, 3, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { BotData = bot };
        var bots = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_bots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(users)!;
        bots[bot.Id] = botUser;

        var habbo = new Habbo { Id = actorId, CurrentRoom = room, Access = EditorTestSupport.Access([]) };
        var (client, _) = HabbiconTestSupport.Client(habbo);
        var store = new RecordingStore();

        return (new(store, null!), store, bot, client);
    }

    private sealed class RecordingStore : IBotManagementStore
    {
        public bool Fail { get; set; }
        public int SpeechWrites { get; private set; }
        public (int BotId, uint RoomId, bool Automatic, int Interval, bool Mix)? LastSpeech { get; private set; }
        public List<(int BotId, string Mode)> WalkingModes { get; } = [];
        public BotPlacementData Place(int botId, int ownerId, uint roomId, int x, int y) => throw new NotSupportedException();
        public void PickUp(int botId, uint roomId) => throw new NotSupportedException();
        public void SaveAppearance(int botId, uint roomId, string look, string gender) => throw new NotSupportedException();
        public IReadOnlyList<string> SaveSpeech(int botId, uint roomId, IReadOnlyList<string> speech, bool automatic, int interval, bool mix)
        {
            SpeechWrites++;
            LastSpeech = (botId, roomId, automatic, interval, mix);

            return speech;
        }
        public void SaveName(int botId, uint roomId, string name) => throw new NotSupportedException();
        public void SaveWalkingMode(int botId, uint roomId, string mode)
        {
            if (Fail) {
                throw new InvalidOperationException("forced failure");
            }

            WalkingModes.Add((botId, mode));
        }
    }
}
