using Microsoft.IO;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.GameClients;

[Singleton]
public interface IPacketFactory
{
    IIncomingPacket CreateIncomingPacket(RecyclableMemoryStream stream);
    IOutgoingPacket CreateOutgoingPacket(RecyclableMemoryStream stream);
}
