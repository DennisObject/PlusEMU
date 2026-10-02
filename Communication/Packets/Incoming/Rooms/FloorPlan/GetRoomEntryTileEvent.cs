using Plus.Communication.Packets.Outgoing.Rooms.FloorPlan;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Rooms.FloorPlan;

internal class GetRoomEntryTileEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().InRoom)
            return Task.CompletedTask;
        var room = session.GetHabbo().CurrentRoom;
        var model = room?.GetGameMap().Model;
        if (model == null)
            return Task.CompletedTask;

        session.Send(new RoomEntryTileComposer(model.DoorX, model.DoorY, model.DoorOrientation));
        return Task.CompletedTask;
    }
}
