using Microsoft.Extensions.Logging;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

public sealed class GetHabbiconInfoEvent(IHabbiconService service, ILogger<HabbiconRequest> logger) : HabbiconRequest(service, logger)
{
    protected override bool Info => true;
}
