using Plus.Communication.Packets.Outgoing.Groups;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class RemoveGroupFavouriteEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.GetHabbo().HabboStats.FavouriteGroupId = 0;
        if (session.GetHabbo().InRoom)
        {
            var user = session.GetHabbo().CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
            if (user != null)
                session.GetHabbo().CurrentRoom.SendPacket(new UpdateFavouriteGroupComposer(FavouriteGroupSnapshot.Capture(null, user.VirtualId)));
            session.GetHabbo().CurrentRoom.SendPacket(new RefreshFavouriteGroupComposer(session.GetHabbo().Id));
        }
        else
            session.Send(new RefreshFavouriteGroupComposer(session.GetHabbo().Id));
        return Task.CompletedTask;
    }
}