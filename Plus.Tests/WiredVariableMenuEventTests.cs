using Plus.HabboHotel.Items.Wired.Variables;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
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
        typeof(Room).GetField("_wiredComponent", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(room,
            new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance));
        RoomPacketEvent[] handlers = [new WiredUserVariableUpdateEvent(new WiredVariableMenuService()), new WiredUserVariableManageEvent(new WiredVariableMenuService()), new WiredUserVariablesRequestEvent(new WiredVariableMenuService()), new WiredAllVariablesRequestEvent(new WiredVariableMenuService()), new WiredVariableHashesEvent(new WiredVariableMenuService()),
            new WiredVariableHoldersRequestEvent(new WiredVariableMenuService()), new WiredVariableHoldersPageEvent(new WiredVariableMenuService())];
        foreach (var handler in handlers)
        {
            var packet = Packet(123); await handler.Parse(room, null!, packet);
            Assert.Equal(4, packet.Buffer.Length);
        }
    }
    [Fact]
    public void WritesRejectMalformedTargetsTokensActionsAndTruncatedOrTrailingPayloads()
    {
        Assert.True(WiredUserVariableUpdateEvent.TryRead(Packet(1, -2, 12, 50), false, out var update));
        Assert.Equal(-2, update!.TargetId);
        Assert.True(WiredUserVariableUpdateEvent.TryRead(Packet(0, 123, 0, 4, "internal:@handitem"), false, out _));
        Assert.True(WiredUserVariableUpdateEvent.TryRead(Packet(2, 0, 0, 12, 0), true, out var clear));
        Assert.Equal(2, clear!.Action);
        foreach (var packet in new[] { Packet(2, 1, 12, 0), Packet(0, 1, -1, 0), Packet(0, 1, 0, 0),
            Packet(0, 1, 12, 0, "internal:@id"), Packet(0, 1, 0, 0, "custom:12"), Packet(0, 1, 12), Packet(0, 1, 12, 0, "", 1) })
            Assert.False(WiredUserVariableUpdateEvent.TryRead(packet, false, out _));
        Assert.False(WiredUserVariableUpdateEvent.TryRead(Packet(3, 0, 0, 12, 0), true, out _));
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
