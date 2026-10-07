using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms.Engine;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Polls;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Messenger;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public async Task RoomEntryHandlerOnlyDelegatesWithoutReadingTheFrameOrRoom()
    {
        var entry = new RecordingRoomEntry();
        _client.GetHabbo().CurrentRoom = null;
        var packet = ClientPacket();
        await new GetRoomEntryDataEvent(entry).Parse(_client, packet);
        Assert.Same(_client, entry.Session);
        Assert.Equal(1, entry.Calls);
        Assert.False(packet.HasDataRemaining());
        Assert.Empty(_client.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuccessfulRoomEntryRetainsPresentationOrderAndSamplesFloodClockOnce(bool activeFlood)
    {
        var habbo = _client.GetHabbo();
        habbo.Look = "hd-180-1.ch-210-66";
        habbo.Gender = "M";
        habbo.Motto = "hello";
        habbo.HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 17, 0, 0, "", 0);
        var clock = new EntryClock();
        habbo.Messenger = new HabboMessenger([], [], [], clock);
        habbo.Effects = new Plus.HabboHotel.Users.Effects.EffectsComponent(clock);
        clock.Reads = 0;
        var changes = 0;
        habbo.Messenger.StatusUpdated += (_, _) => changes++;
        habbo.FloodUntil = clock.Now.AddMilliseconds(activeFlood ? 1001 : 0);
        var reminders = 0;
        var quests = Proxy<IQuestManager>((method, args) =>
        {
            Assert.Equal(nameof(IQuestManager.QuestReminder), method);
            Assert.Same(_client, args[0]);
            Assert.Equal(17, args[1]);
            Assert.Contains(ServerPacketHeader.ItemsComposer, _client.Sent);
            Assert.DoesNotContain(ServerPacketHeader.RoomEntryInfoComposer, _client.Sent);
            reminders++;

            return null;
        });

        var polls = Proxy<IRoomPollService>((method, args) =>
        {
            Assert.Equal(nameof(IRoomPollService.Offer), method);
            Assert.Same(_client, args[0]);
            Assert.Contains(ServerPacketHeader.RoomEventComposer, _client.Sent);
            return null;
        });
        new RoomEntryService(quests, clock, polls).Enter(_client);

        Assert.NotNull(_room.GetRoomUserManager().GetRoomUserByHabbo(7));
        Assert.Equal(1, reminders);
        Assert.Equal(1, changes);
        Assert.Equal(1, clock.Reads);
        var tail = _client.Sent.SkipWhile(id => id != ServerPacketHeader.RoomEntryInfoComposer).ToArray();
        var expected = new List<uint>
        {
            ServerPacketHeader.RoomEntryInfoComposer,
            ServerPacketHeader.RoomVisualizationSettingsComposer,
            ServerPacketHeader.UserChangeComposer,
            ServerPacketHeader.RoomEventComposer
        };

        if (activeFlood) {
            expected.Add(ServerPacketHeader.FloodControlComposer);
        }

        Assert.Equal(expected, tail);

        if (activeFlood) {
            var packet = new FlashIncomingPacket { Buffer = _client.Packets.Last().Body };
            Assert.Equal(2, packet.ReadInt());
            Assert.False(packet.HasDataRemaining());
        }
    }

    [Fact]
    public void FailedAdmissionRemovesExistingActorAndDoesNotPresentOrReadEntryClock()
    {
        var user = Viewer();
        user.UserId = 7;
        user.InternalRoomId = user.VirtualId;
        var clock = new EntryClock();
        var service = new RoomEntryService(null!, clock, null!);
        service.Enter(_client);
        Assert.Null(_client.GetHabbo().CurrentRoom);
        Assert.DoesNotContain(user, _room.GetRoomUserManager().GetRoomUsers());
        Assert.DoesNotContain(ServerPacketHeader.RoomEntryInfoComposer, _client.Sent);
        Assert.DoesNotContain(ServerPacketHeader.RoomEventComposer, _client.Sent);
        Assert.Equal(0, clock.Reads);
        _client.Sent.Clear();
        service.Enter(_client);
        Assert.Empty(_client.Sent);
        Assert.Equal(0, clock.Reads);
    }

    private sealed class RecordingRoomEntry : IRoomEntryService
    {
        public GameClient? Session;
        public int Calls;
        public void Enter(GameClient session)
        {
            Session = session;
            Calls++;
        }
    }

    private sealed class EntryClock : TimeProvider
    {
        public readonly DateTimeOffset Now = new(2040, 1, 2, 3, 4, 5, TimeSpan.FromMinutes(330));
        public int Reads;
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return Now;
        }
    }
}
