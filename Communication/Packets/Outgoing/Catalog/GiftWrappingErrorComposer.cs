using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class GiftWrappingErrorComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.GiftWrappingErrorComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // This notification currently sends an empty payload.
    }
}
