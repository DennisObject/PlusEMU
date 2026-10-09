using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.Communication.Packets.Outgoing.WiredVariables;

public sealed class WiredVariableInspectionDataComposer(WiredVariableInspectionRequest request, WiredVariableInspectionStatus status,
    IReadOnlyList<WiredVariableInspectionEntry> entries) : IServerPacket
{
    private readonly WiredVariableInspectionEntry[] _entries = Capture(status, entries);
    public uint MessageId => ServerPacketHeader.WiredVariableInspectionDataComposer;

    private static WiredVariableInspectionEntry[] Capture(WiredVariableInspectionStatus result, IReadOnlyList<WiredVariableInspectionEntry> values)
    {
        if (result != WiredVariableInspectionStatus.Success) {
            return [];
        }

        if (values.Count != WiredVariableInspectionService.Tokens.Count
            || !values.Select(entry => entry.Token).Order().SequenceEqual(WiredVariableInspectionService.Tokens.Order())) {
            throw new ArgumentException("A successful wall inspection requires every numeric token exactly once.", nameof(values));
        }

        return values.ToArray();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInt(1);
        packet.WriteInt(request.RequestId);
        packet.WriteUInteger(request.RoomId);
        packet.WriteInt(request.Target);
        packet.WriteInt(request.EntityId);
        packet.WriteInt(request.Domain);
        packet.WriteInt((int)status);
        packet.WriteInt(_entries.Length);

        foreach (var entry in _entries) {
            packet.WriteString(entry.Token);
            packet.WriteBoolean(true);
            WiredVariableWireProtocol.WriteValue(packet, entry.Value, exact: true);
        }
    }
}
