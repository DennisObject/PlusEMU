using System.Reflection;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Groups;
using Plus.Database;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class OfficialUserWireTests
{
    [Fact]
    public void SnapshotCaptureRequiresActiveHumanAndFreezesWireFields()
    {
        var habbo = new Habbo
        {
            Id = 7, Username = "Dennis", Motto = "hi", Look = "hd-180-1", Gender = "M",
            HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 12, 0, 0, 0, "", 0)
        };
        var client = new TestClient();
        client.SetHabbo(habbo);
        var active = true;
        var clients = Proxy<IGameClientManager>((method, _) => method == "GetClientByUserId" && active ? client : null);
        var service = new RoomUserSnapshotService(Proxy<IGroupManager>((_, _) => null), clients,
            Proxy<ICacheManager>((_, _) => null), Proxy<IDatabase>((_, _) => null));
        var user = new RoomUser(7, 1, 4, null) { X = 2, Y = 3, Z = 1.5, RotBody = 2 };

        var snapshot = Assert.IsType<RoomUserSnapshot>(service.Capture(user));
        active = false;
        Assert.Null(service.Capture(user));
        habbo.Username = "changed";
        user.X = 99;
        var packet = new RecordingPacket();
        new UsersComposer(snapshot).Compose(packet);

        Assert.Equal(7, packet.Writes[1]);
        Assert.Equal("Dennis", packet.Writes[2]);
        Assert.Equal(2, packet.Writes[6]);
    }

    [Fact]
    public void RoomUserWritesTheOfficialLayout()
    {
        var habbo = new Habbo
        {
            Id = 7,
            Username = "Dennis",
            Motto = "hi",
            Look = "hd-180-1",
            Gender = "M",
            HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 12, 0, 0, 0, "", 0)
        };
        var client = new TestClient();
        client.SetHabbo(habbo);
        var user = new RoomUser(7, 1, 4, null)
        {
            X = 2,
            Y = 3,
            Z = 1.5,
            RotBody = 2
        };
        typeof(RoomUser).GetField("_mClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(user, client);
        var packet = new RecordingPacket();

        new UsersComposer(new RoomUserSnapshot(7, "Dennis", "hi", "hd-180-1", 4, 2, 3, "1.5", 2, 1,
            "m", 0, "", 12, false, 0, 0, "", false, false)).Compose(packet);

        Assert.Equal(new object[]
        {
            1,
            7, "Dennis", "hi", "hd-180-1", 4, 2, 3, "1.5", 2, 1,
            "m", 0, 0, "", "", 12, false,
            "", 0
        }, packet.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PetAndBotPacketsFreezeTheSourceCollection(bool isPet)
    {
        var snapshot = new RoomUserSnapshot(10, "Helper", "hello", "figure", 4, 2, 3, "1.5", 0,
            isPet ? 2 : 4, "m", 0, "", 0, isPet, 13, 7, "Dennis", true, false);
        var source = new[] { snapshot };
        var composer = new UsersComposer(source);
        var expected = new List<object> { 1, 10, "Helper", "hello", "figure", 4, 2, 3, "1.5", 0, isPet ? 2 : 4 };
        if (isPet) expected.AddRange(new object[] { 13, 7, "Dennis", 1, true, false, 0, 0, "" });
        else expected.AddRange(new object[] { "m", 7, "Dennis", 5, (short)1, (short)2, (short)3, (short)4, (short)5 });
        expected.AddRange(new object[] { "", 0 });
        source[0] = snapshot with { Name = "changed", X = 99, OwnerName = "changed" };
        Assert.Equal(expected, Compose(composer));
        Assert.Equal(expected, Compose(composer));
    }

    [Fact]
    public void ChatVariantsWriteTheOfficialLayout()
    {
        var chat = Compose(new ChatComposer(4, "hello", 0, 34));
        var shout = Compose(new ShoutComposer(4, "hello", 0, 34));
        var whisper = Compose(new WhisperComposer(4, "hello", 0, 34));

        Assert.Equal(new object[] { 4, "hello", 0, 34, 0, 5 }, chat);
        Assert.Equal(chat, shout);
        Assert.Equal(chat, whisper);
    }

    [Fact]
    public void UnitInfoEndsAtTheAchievementScore()
    {
        var habbo = new Habbo
        {
            Motto = "hi",
            Look = "hd-180-1",
            Gender = "M",
            HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 12, 0, 0, 0, "", 0)
        };
        var client = new TestClient();
        client.SetHabbo(habbo);
        var user = new RoomUser(7, 1, 4, null);
        typeof(RoomUser).GetField("_mClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(user, client);
        var packet = new RecordingPacket();

        new UserChangeComposer(AvatarChangeSnapshot.Capture(user, true)).Compose(packet);

        Assert.Equal(new object[] { -1, "hd-180-1", "M", "hi", 12 }, packet.Writes);
        var composer = new UserChangeComposer(AvatarChangeSnapshot.Capture(user, true));
        habbo.Look = "changed";
        habbo.Motto = "changed";
        habbo.Gender = "F";
        habbo.HabboStats.AchievementPoints = 99;
        Assert.Equal(new object[] { -1, "hd-180-1", "M", "hi", 12 }, Compose(composer));
        Assert.Equal(new object[] { -1, "hd-180-1", "M", "hi", 12 }, Compose(composer));
    }

    private static object[] Compose(IServerPacket composer)
    {
        var packet = new RecordingPacket();
        composer.Compose(packet);
        return packet.Writes.ToArray();
    }

    private static T Proxy<T>(Func<string, object?[], object?> callback) where T : class
    {
        var proxy = DispatchProxy.Create<T, CallbackProxy>();
        ((CallbackProxy)(object)proxy).Callback = callback;
        return proxy;
    }

    public class CallbackProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Callback { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Callback(targetMethod!.Name, args ?? []);
    }

    private sealed class TestClient : GameClient
    {
        public TestClient() : base(TestGameServer.Instance, new Plus.Communication.Flash.FlashPacketFactory(), TestLogging.GameClient)
        {
        }

        internal override (bool Complete, bool Malformed, uint MessageId, int HeaderLength, int Length) GetMessageIdAndPacketLength(ReadOnlyMemory<byte> buffer) =>
            (true, false, 0, 0, 0);

        public override void CreateHeader(Memory<byte> memory, uint messageId)
        {
        }
    }

    private sealed class RecordingPacket : IOutgoingPacket
    {
        public List<object> Writes { get; } = [];
        public int MessageId { get; set; }
        public ReadOnlyMemory<byte> Buffer => ReadOnlyMemory<byte>.Empty;
        public void WriteByte(byte value) => Writes.Add(value);
        public void WriteShort(short value) => Writes.Add(value);
        public void WriteInt(int value) => Writes.Add(value);
        public void WriteInteger(int value) => Writes.Add(value);
        public void WriteUInt(uint value) => Writes.Add(value);
        public void WriteUInteger(uint value) => Writes.Add(value);
        public void WriteBool(bool value) => Writes.Add(value);
        public void WriteBoolean(bool value) => Writes.Add(value);
        public void WriteString(string value) => Writes.Add(value ?? "");
        public void WriteDouble(double value) => Writes.Add(value);
    }
}
