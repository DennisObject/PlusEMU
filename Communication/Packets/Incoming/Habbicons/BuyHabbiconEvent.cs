using Microsoft.Extensions.Logging;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

public sealed class BuyHabbiconEvent(IHabbiconService service, ILogger<HabbiconRequest> logger) : HabbiconRequest(service, logger)
{
    protected override HabbiconAction? Action => HabbiconAction.Buy;
}
