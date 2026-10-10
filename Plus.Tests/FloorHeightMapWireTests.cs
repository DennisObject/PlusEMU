using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.GameClients;
using Xunit;

namespace Plus.Tests;

public class FloorHeightMapWireTests
{
    [Fact]
    public void FloorHeightMapWritesTheHideAndCameraTail()
    {
        var packet = new RecordingPacket();
        var hide = new FloorHeightMapComposer.AreaHide(9, true, 1, 2, 3, 4, false);

        new FloorHeightMapComposer("0x\r00", -1, true, new[] { hide }, 5, 6, 1.5f).Compose(packet);

        Assert.Equal(new object[]
        {
            true, -1, "0x\r00", 1,
            9, true, 1, 2, 3, 4, false,
            5, 6, BitConverter.SingleToInt32Bits(1.5f)
        }, packet.Writes);

        var empty = new RecordingPacket();
        new FloorHeightMapComposer("0", -1).Compose(empty);
        Assert.Equal(new object[] { true, -1, "0", 0, 0, 0, 0 }, empty.Writes);
    }

    private sealed class RecordingPacket : IOutgoingPacket
    {
        public List<object> Writes { get; } = new();
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
