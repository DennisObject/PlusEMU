using Plus.Communication.Packets.Outgoing.Groups;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.Core.Settings;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class GetGroupCreationWindowEvent(ISettingsManager settings) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var rooms = RoomFactory.GetRoomsDataByOwnerSortByName(session.GetHabbo().Id).Where(x => x.Group == null).ToList();
        var price = Convert.ToInt32(settings.TryGetValue("catalog.group.purchase.cost"));
        session.Send(new GroupCreationWindowComposer(rooms, price));
        return Task.CompletedTask;
    }
}
