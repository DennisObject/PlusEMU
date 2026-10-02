using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableMenuEventTests
{
    [Fact]
    public void HashParserBoundsCountAndRejectsDuplicateTruncatedOrTrailingPayloads()
    {
        Assert.True(WiredVariableHashesEvent.TryReadHashes(Packet(1, "user:10", 123), out var known));
        Assert.Equal(123, known["user:10"]);
        foreach (var packet in new[] { Packet(-1), Packet(4097), Packet(1), Packet(0, 1),
            Packet(2, "user:10", 1, "user:10", 2), Packet(1, new string('x', 65), 1) })
            Assert.False(WiredVariableHashesEvent.TryReadHashes(packet, out _));
    }
    [Fact]
    public async Task AllMenuHandlersRejectUnauthorizedClientsBeforeReadingOrOpeningDatabase()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        RoomPacketEvent[] handlers = [new WiredUserVariablesRequestEvent(), new WiredAllVariablesRequestEvent(), new WiredVariableHashesEvent(),
            new WiredVariableHoldersRequestEvent(), new WiredVariableHoldersPageEvent()];
        foreach (var handler in handlers)
        {
            var packet = Packet(123); await handler.Parse(room, null!, packet);
            Assert.Equal(4, packet.Buffer.Length);
        }
    }
    private static FlashIncomingPacket Packet(params object[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            if (value is int number)
            { var bytes = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, number); stream.Write(bytes); }
            else
            {
                var bytes = Encoding.UTF8.GetBytes((string)value); var length = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)bytes.Length)); stream.Write(length); stream.Write(bytes);
            }
        }
        return new() { Buffer = stream.ToArray() };
    }
}
