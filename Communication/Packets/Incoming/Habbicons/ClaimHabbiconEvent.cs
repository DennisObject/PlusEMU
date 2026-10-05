using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

public sealed class ClaimHabbiconEvent(IHabbiconPresentationService presentation) : HabbiconRequest(presentation)
{
    protected override HabbiconAction? Action => HabbiconAction.Claim;
}
