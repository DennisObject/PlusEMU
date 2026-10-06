using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public sealed class AllowedChatStylesComposer(IReadOnlyList<int> styleIds) : IServerPacket
{
    private readonly IReadOnlyList<int> _captured = styleIds.ToArray();
    public uint MessageId => ServerPacketHeader.AllowedChatStylesComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_captured.Count);
        foreach (var id in _captured) packet.WriteInteger(id);
    }
}
