using System.Runtime.CompilerServices;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(false, ServerPacketHeader.ChatComposer)]
    [InlineData(true, ServerPacketHeader.ShoutComposer)]
    public void CapturedEmotionManagerProducesExactChatWireWithoutGlobals(bool shout, uint expectedHeader)
    {
        var emotions = new TestChatEmotions(message => message == "happy" ? 7 : throw new InvalidOperationException(message));
        var manager = new RoomUserManager(_room, TestRoomUserStore.Instance, TimeProvider.System,
            TestRewardProgress.Unused, emotions, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
        Set("_roomUserManager", manager);
        _room.WordFilterList = [];
        _client.GetHabbo().IgnoresComponent = new([]);
        Assert.True(manager.AddAvatarToRoom(_client));
        _client.Sent.Clear();
        _client.Packets.Clear();
        _gameField.SetValue(null, Proxy<IGame>((method, _) => throw new InvalidOperationException(method)));

        var actor = Assert.IsType<RoomUser>(manager.GetRoomUserByHabbo(7));
        actor.OnChat(34, "happy", shout);

        var captured = Assert.Single(_client.Packets);
        Assert.Equal(expectedHeader, captured.Header);
        var packet = new FlashIncomingPacket { Buffer = captured.Body };
        Assert.Equal(actor.VirtualId, packet.ReadInt());
        Assert.Equal("happy", packet.ReadString());
        Assert.Equal(7, packet.ReadInt());
        Assert.Equal(34, packet.ReadInt());
        Assert.Equal(0, packet.ReadInt());
        Assert.Equal(5, packet.ReadInt());
        Assert.False(packet.HasDataRemaining());
        Assert.Equal(["happy"], emotions.Messages);
    }

    [Fact]
    public void CarryItemPublishesBeforeOneRewardForEachDistinctPositiveHumanItem()
    {
        var rewards = new TestRewardProgress((session, action, amount) =>
        {
            Assert.Equal(ServerPacketHeader.CarryObjectComposer, _client.Sent.Last());
            Assert.Same(_client, session);
            Assert.Equal(RewardTrackActions.FindHandItem, action);
            Assert.Equal(1, amount);
        });
        var manager = new RoomUserManager(_room, TestRoomUserStore.Instance, TimeProvider.System,
            rewards, TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
        Set("_roomUserManager", manager);
        Assert.True(manager.AddAvatarToRoom(_client));
        _client.Sent.Clear();
        _client.Packets.Clear();

        WithUnavailableRewardManager(() =>
        {
            var actor = Assert.IsType<RoomUser>(manager.GetRoomUserByHabbo(7));
            actor.CarryItem(4);
            actor.CarryItem(4);
            actor.CarryItem(0);
            actor.CarryItem(5);

            var bot = new RoomUser(0, RoomId, 91, _room, null, TestChatEmotions.Unused, rewards)
            {
                BotData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot))
            };
            bot.CarryItem(6);

            var detached = new RoomUser(7, RoomId, 92, _room, _client, TestChatEmotions.Unused, rewards);
            detached.Dispose();
            detached.CarryItem(7);
        });

        Assert.Equal([(_client, RewardTrackActions.FindHandItem, 1), (_client, RewardTrackActions.FindHandItem, 1)], rewards.Calls);
        Assert.Equal(5, _client.Packets.Count(packet => packet.Header == ServerPacketHeader.CarryObjectComposer));
    }

    [Fact]
    public void CanonicalRoomRefusalDoesNotResolveEmotionOrPublishChat()
    {
        var emotions = new TestChatEmotions(_ => throw new InvalidOperationException("Detached chat resolved emotions."));
        var actor = new RoomUser(7, RoomId, 93, _room, _client, emotions, TestRewardProgress.Unused);
        _client.GetHabbo().CurrentRoom = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        _client.Sent.Clear();
        _client.Packets.Clear();
        _gameField.SetValue(null, Proxy<IGame>((method, _) => throw new InvalidOperationException(method)));

        actor.OnChat(1, "stale", false);

        Assert.Empty(emotions.Messages);
        Assert.Empty(_client.Sent);
        Assert.False(_client.GetHabbo().HasSpoken);
    }
}
