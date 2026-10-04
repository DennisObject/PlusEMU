using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public sealed class AllowedChatStylesComposer(IReadOnlyList<int> styleIds) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.AllowedChatStylesComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(styleIds.Count);
        foreach (var id in styleIds) packet.WriteInteger(id);
    }
}
