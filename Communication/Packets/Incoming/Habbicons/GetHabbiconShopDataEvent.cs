using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

public sealed class GetHabbiconShopDataEvent(IHabbiconPresentationService presentation) : HabbiconRequest(presentation)
{
}
