using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableCatalogWireTests
{
    [Fact]
    public void LegacySnapshotKeepsDefinitionHolderAndContextSectionsInClientOrder()
    {
        var definitions = new[]
        {
            new WiredVariableDescription(new(10, 1, 5, "score", WiredVariableTarget.User, WiredVariableAvailability.Persistent, true), true, false),
            new WiredVariableDescription(new(11, 1, 5, "capture", WiredVariableTarget.Context, WiredVariableAvailability.RoomActive, false), false, true)
        };
        var snapshot = new WiredVariableMenuSnapshot(1, definitions, [new(new(10, WiredVariableTarget.User, 901), "", new(25, DateTimeOffset.FromUnixTimeMilliseconds(1000), DateTimeOffset.FromUnixTimeMilliseconds(2000)))]);
        var packet = new Packet();
        new WiredUserVariablesDataComposer(snapshot).Compose(packet);
        Assert.Equal(new object[] { 1u, 1, 10u, "score", true, 10, false, false,
            1, 901, 1, 10u, true, 25, 1, 2, 0, 0, 0, 0, 1, 11u, "capture", false, 1, false, true }, packet.Values);
    }
    [Fact]
    public void CatalogMetadataUsesMenuTargetCodesAndRealCapabilities()
    {
        var variable = new WiredVariableDescription(new(10, 1, 5, "score", WiredVariableTarget.Global,
            WiredVariableAvailability.Persistent, true), true, false);
        var packet = new Packet();
        new WiredAllVariablesDiffComposer(new(42, true, [], [variable])).Compose(packet);
        Assert.Equal(new object[] { 42, true, 0, 1, variable.Hash, "room:10", 0, "score", 10, 0,
            true, false, true, true, false, false, true, true, false }, packet.Values);
    }
    [Fact]
    public void PageUsesStableUserIdAndHighLowMillisecondsInClientOrder()
    {
        var holder = new WiredVariableStoredHolder(new(10, WiredVariableTarget.User, 901), "player",
            new(25, DateTimeOffset.FromUnixTimeMilliseconds(4294967297L), null));
        var packet = new Packet();
        new WiredVariableHoldersPageComposer("user:10", new(25, 2, 10, [holder]), 1, 0).Compose(packet);
        Assert.Equal(new object[] { "user:10", 25, 2, 10, 1, 1, 901, "player", 25, 1, 1,
            "19/02/1970 17:02:47", 0, 0, "", 1, 0 }, packet.Values);
    }
    [Fact]
    public void EpochAndPreEpochKeepRawMillisecondsButOmitLegacyText()
    {
        var holders = new[]
        {
            new WiredVariableStoredHolder(new(10, WiredVariableTarget.User, 901), "epoch",
                new(1, DateTimeOffset.UnixEpoch, null)),
            new WiredVariableStoredHolder(new(10, WiredVariableTarget.User, 902), "before",
                new(2, DateTimeOffset.FromUnixTimeMilliseconds(-1), null))
        };
        var packet = new Packet();
        new WiredVariableHoldersPageComposer("user:10", new(2, 1, 10, holders), 1, 0).Compose(packet);
        Assert.Equal(new object[] { "user:10", 2, 1, 10, 2,
            1, 901, "epoch", 1, 0, 0, "", 0, 0, "",
            1, 902, "before", 2, -1, -1, "", 0, 0, "", 1, 0 }, packet.Values);
    }
    private sealed class Packet : IOutgoingPacket
    {
        public List<object> Values { get; } = [];
        public int MessageId
        {
            get; set;
        }
        public ReadOnlyMemory<byte> Buffer => default;
        public void WriteByte(byte value) => Values.Add(value);
        public void WriteShort(short value) => Values.Add(value);
        public void WriteInt(int value) => Values.Add(value);
        public void WriteInteger(int value) => Values.Add(value);
        public void WriteUInt(uint value) => Values.Add(value);
        public void WriteUInteger(uint value) => Values.Add(value);
        public void WriteBool(bool value) => Values.Add(value);
        public void WriteBoolean(bool value) => Values.Add(value);
        public void WriteString(string value) => Values.Add(value);
        public void WriteDouble(double value) => Values.Add(value);
    }
}
