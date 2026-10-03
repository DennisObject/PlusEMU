using System.Reflection;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class OctaneCustomizationWireTests
{
    [Fact]
    public void RoomUserEndsWithNickIconBeforeTheOfficialRoomEntryTail()
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

        new UsersComposer(user).Compose(packet);

        Assert.Equal(new object[]
        {
            1,
            7, "Dennis", "hi", 0, 0, 0, 0, "hd-180-1", 4, 2, 3, "1.5", 2, 1,
            "m", 0, 0, "", "", 12, false, "",
            "", 0, 0
        }, packet.Writes);
    }

    [Fact]
    public void ChatVariantsWriteNickIconThenBubbleWidth()
    {
        var chat = Compose(new ChatComposer(4, "hello", 0, 34));
        var shout = Compose(new ShoutComposer(4, "hello", 0, 34));
        var whisper = Compose(new WhisperComposer(4, "hello", 0, 34));

        Assert.Equal(new object[] { 4, "hello", 0, 34, 0, "", 5, "", -1 }, chat);
        Assert.Equal(chat, shout);
        Assert.Equal(chat, whisper);
    }

    [Fact]
    public void UnitInfoWritesTheRetainedCustomizationTail()
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

        new UserChangeComposer(user, true).Compose(packet);

        Assert.Equal(new object[] { -1, "hd-180-1", "M", "hi", 12, 0, 0, 0, 0, "", 0 }, packet.Writes);
    }

    private static object[] Compose(IServerPacket composer)
    {
        var packet = new RecordingPacket();
        composer.Compose(packet);
        return packet.Writes.ToArray();
    }

    private sealed class TestClient : GameClient
    {
        public TestClient() : base(null!, null!)
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
