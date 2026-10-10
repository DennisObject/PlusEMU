using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;
using static Plus.Tests.HabbiconTestSupport;

namespace Plus.Tests;

public class WiredVariableInspectionWireTests
{
    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(9007199254740993L)]
    [InlineData(-9007199254740993L)]
    public void WallInspectionWireCopiesCompleteExactValuesAndCorrelatesPositiveRouting(long value)
    {
        var values = WiredVariableInspectionService.Tokens.Select(token => new WiredVariableInspectionEntry(token, value)).ToArray();
        var composer = new WiredVariableInspectionDataComposer(new(123, 42, 1, 301, 1), WiredVariableInspectionStatus.Success, values);
        values[0] = new("@id", 99);
        var packet = new RecordingPacket();
        composer.Compose(packet);
        Assert.Equal(9483u, composer.MessageId);
        Assert.Equal(new object[] { 1, 123, 42u, 1, 301, 1, 0, 9 }, packet.Writes.Take(8));
        var entries = packet.Writes.Skip(8).Chunk(4).ToArray();
        Assert.Equal(9, entries.Length);

        foreach (var (token, index) in WiredVariableInspectionService.Tokens.Select((token, index) => (token, index))) {
            Assert.Equal(new object[] { token, true, unchecked((int)(value >> 32)), unchecked((int)value) }, entries[index]);
        }
    }

    [Fact]
    public void WallInspectionCannotSerializePartialOrDuplicateSuccessAndRefusalStripsEveryValue()
    {
        var request = new WiredVariableInspectionRequest(1, 42, 1, 301, 1);
        Assert.Throws<ArgumentException>(() => new WiredVariableInspectionDataComposer(request, WiredVariableInspectionStatus.Success, []));
        var duplicate = Enumerable.Repeat(new WiredVariableInspectionEntry("@id", -301), 9).ToArray();
        Assert.Throws<ArgumentException>(() => new WiredVariableInspectionDataComposer(request, WiredVariableInspectionStatus.Success, duplicate));

        foreach (var status in new[] { WiredVariableInspectionStatus.Refused, WiredVariableInspectionStatus.Unavailable, WiredVariableInspectionStatus.Unsupported }) {
            var packet = new RecordingPacket();
            new WiredVariableInspectionDataComposer(request, status, duplicate).Compose(packet);
            Assert.Equal(new object[] { 1, 1, 42u, 1, 301, 1, (int)status, 0 }, packet.Writes);
        }
    }

    [Fact]
    public async Task WallInspectionMalformedFramesNeverReachServiceAndValidFrameConsumesEof()
    {
        var service = new InspectionRecorder();
        var handler = new WiredVariableInspectionRequestEvent(service);
        var valid = Incoming(1, 1, 42, 1, 301, 1);
        var bytes = valid.Buffer.ToArray();

        for (var length = 0; length < bytes.Length; length++) {
            await handler.Parse(null!, null!, new Plus.Communication.Flash.FlashIncomingPacket { Buffer = bytes[..length] });
        }

        foreach (var frame in new[] { Incoming(2, 1, 42, 1, 301, 1), Incoming(1, 0, 42, 1, 301, 1),
            Incoming(1, 1, 0, 1, 301, 1), Incoming(1, 1, 42, 1, -301, 1), Incoming(1, 1, 42, 1, 301, 1, 99) }) {
            await handler.Parse(null!, null!, frame);
        }

        Assert.Empty(service.Requests);
        await handler.Parse(null!, null!, valid);
        Assert.Equal(new(1, 42, 1, 301, 1), Assert.Single(service.Requests));
        Assert.False(valid.HasDataRemaining());
    }

    [Fact]
    public void WallInspectionConcreteHandlerRegistersOnlyItsNamedDirectPair()
    {
        var handler = new WiredVariableInspectionRequestEvent(new InspectionRecorder());
        using var manager = new PacketManager([handler], NullLogger<PacketManager>.Instance);
        var registered = (Dictionary<uint, IPacketEvent>)typeof(PacketManager).GetField("_incomingPackets", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
        Assert.Single(registered);
        Assert.Same(handler, registered[10112]);

        var incoming = Assert.Single(typeof(ClientPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static),
            field => field.IsLiteral && field.GetRawConstantValue() is uint value && value == 10112);
        var outgoing = Assert.Single(typeof(ServerPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static),
            field => field.IsLiteral && field.GetRawConstantValue() is uint value && value == 9483);
        Assert.Equal(nameof(ClientPacketHeader.WiredVariableInspectionRequestEvent), incoming.Name);
        Assert.Equal(nameof(ServerPacketHeader.WiredVariableInspectionDataComposer), outgoing.Name);
    }

    private sealed class InspectionRecorder : IWiredVariableInspectionService
    {
        public List<WiredVariableInspectionRequest> Requests { get; } = [];
        public void Read(Room room, GameClient session, WiredVariableInspectionRequest request) => Requests.Add(request);
    }
}
