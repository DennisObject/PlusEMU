using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Commands.Events;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class EventAlertCommandTests : IDisposable
{
    private static readonly FieldInfo LastEvent = typeof(EventAlertCommand).GetField("_lastEvent", BindingFlags.Static | BindingFlags.NonPublic)!;

    public EventAlertCommandTests() => LastEvent.SetValue(null, null);
    public void Dispose() => LastEvent.SetValue(null, null);

    [Fact]
    public void CooldownPreservesStrictHourBoundaryAndReadsOneUtcInstantPerCall()
    {
        var clock = new Clock(new DateTimeOffset(2040, 1, 1, 12, 0, 0, TimeSpan.FromHours(5.5)));
        var broadcasts = new List<string>();
        var command = new EventAlertCommand(Clients(packet =>
        {
            var output = new HabbiconTestSupport.RecordingPacket();
            packet.Compose(output);
            broadcasts.Add((string)output.Writes[0]);
        }), clock);
        var (client, room, sent) = Client(clock);
        command.Execute(client, room, []);
        Assert.Equal(":follow Alice for events! win prizes!\r\n- Alice", Assert.Single(broadcasts));
        Assert.Empty(sent);

        clock.Now = clock.Now.AddMinutes(59);
        command.Execute(client, room, []);
        Assert.Single(broadcasts);
        var whisper = new FlashIncomingPacket { Buffer = Assert.Single(sent).Payload };
        Assert.Equal(11, whisper.ReadInt());
        Assert.Equal("Event Cooldown! 59 minutes left until another event can be hosted.", whisper.ReadString());

        clock.Now = clock.Now.AddMinutes(1);
        command.Execute(client, room, []);
        Assert.Single(broadcasts);
        Assert.Equal(2, sent.Count);
        clock.Now = clock.Now.AddTicks(1);
        command.Execute(client, room, []);
        Assert.Equal(2, broadcasts.Count);
        Assert.Equal(4, clock.Reads);
    }

    [Fact]
    public void FutureAndMaximumInstantsDoNotOverflowOrBypassCooldown()
    {
        var clock = new Clock(DateTimeOffset.MaxValue);
        var broadcasts = 0;
        var command = new EventAlertCommand(Clients(_ => broadcasts++), clock);
        var (client, room, sent) = Client(clock);
        command.Execute(client, room, []);
        clock.Now = clock.Now.AddHours(-1);
        command.Execute(client, room, []);
        clock.Now = DateTimeOffset.MaxValue;
        command.Execute(client, room, []);
        Assert.Equal(1, broadcasts);
        Assert.Equal(2, sent.Count);
        Assert.Equal(3, clock.Reads);
    }

    [Fact]
    public void ConcurrentCommandInstancesShareOneHotelCooldown()
    {
        var clock = new Clock(DateTimeOffset.Parse("2040-01-01T00:00:00Z"));
        var broadcasts = 0;
        var clients = Clients(_ => Interlocked.Increment(ref broadcasts));
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 7, Username = "Alice" });
        Parallel.For(0, 16, _ => new EventAlertCommand(clients, clock).Execute(client, null!, []));
        Assert.Equal(1, broadcasts);
        Assert.Equal(16, clock.Reads);
    }

    private static IGameClientManager Clients(Action<BroadcastMessageAlertComposer> send) =>
        CatalogSnapshotTestSupport.Proxy<IGameClientManager>((method, arguments) =>
        {
            Assert.Equal("SendPacket", method);
            send(Assert.IsType<BroadcastMessageAlertComposer>(arguments![0]));
            return null;
        });

    private static (GameClient Client, Room Room, List<(uint Header, byte[] Payload)> Sent) Client(TimeProvider clock)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var manager = new RoomUserManager(room, TestRoomUserStore.Instance, clock, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, manager);
        var habbo = new Habbo { Id = 7, Username = "Alice", CurrentRoom = room };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        habbo.Client = client;
        var user = new RoomUser(7, 1, 11, room, client, TestChatEmotions.Unused, TestRewardProgress.Unused) { UserId = 7 };
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        users[user.VirtualId] = user;
        return (client, room, sent);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public int Reads;
        public override DateTimeOffset GetUtcNow() { Interlocked.Increment(ref Reads); return Now; }
    }
}
