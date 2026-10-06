using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

public sealed class UnfavoriteHabbiconEvent(IHabbiconPresentationService presentation) : HabbiconRequest(presentation)
{
    protected override HabbiconAction? Action => HabbiconAction.Unfavorite;
}
