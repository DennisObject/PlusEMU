using Microsoft.IO;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Flash;

public class FlashPacketFactory : IPacketFactory
{
    public IIncomingPacket CreateIncomingPacket(RecyclableMemoryStream stream) => new FlashIncomingPacket(stream);

    public IOutgoingPacket CreateOutgoingPacket(RecyclableMemoryStream stream) => new FlashOutgoingPacket(stream);
}
