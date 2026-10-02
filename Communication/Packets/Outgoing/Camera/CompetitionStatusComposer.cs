using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Camera;

public sealed class CompetitionStatusComposer(bool ok, string errorReason = "") : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CompetitionStatusComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(ok);
        packet.WriteString(errorReason);
    }
}
