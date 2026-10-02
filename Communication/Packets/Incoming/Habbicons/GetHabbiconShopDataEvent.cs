using Microsoft.Extensions.Logging;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

public sealed class GetHabbiconShopDataEvent(IHabbiconService service, ILogger<HabbiconRequest> logger) : HabbiconRequest(service, logger)
{
}
