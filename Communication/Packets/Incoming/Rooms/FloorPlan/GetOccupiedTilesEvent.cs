using Plus.Communication.Packets.Outgoing.Rooms.FloorPlan;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.FloorPlan;

internal class GetOccupiedTilesEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().InRoom)
            return Task.CompletedTask;
        var room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return Task.CompletedTask;

        var items = new List<FloorPlanSave.FloorPlanItem>();
        foreach (var item in room.GetRoomItemHandler().GetFloor)
        {
            if (item?.Definition == null)
                continue;
            var width = item.Definition.Width < 1 ? 1 : item.Definition.Width;
            var length = item.Definition.Length < 1 ? 1 : item.Definition.Length;
            items.Add(new FloorPlanSave.FloorPlanItem(item.Id, item.GetX, item.GetY, item.Rotation, width, length));
        }

        session.Send(new RoomOccupiedTilesComposer(FloorPlanSave.OccupiedTiles(items)));
        return Task.CompletedTask;
    }
}
