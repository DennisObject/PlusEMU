using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

public sealed class GetHabbiconInfoEvent(IHabbiconPresentationService presentation) : HabbiconRequest(presentation)
{
    protected override bool Info => true;
}
