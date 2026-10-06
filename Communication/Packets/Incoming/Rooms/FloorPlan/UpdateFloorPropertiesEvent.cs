using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.FloorPlan;

internal sealed class UpdateFloorPropertiesEvent(IFloorPlanUpdateService service) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var body = FloorPlanRequest.Read(packet);
        service.Update(room, session, new(body.Map, body.DoorFieldsPresent, body.WallHeightPresent, body.Requested));

        return Task.CompletedTask;
    }

    internal static bool TryPersist(Func<int> writeModel, Func<bool> modelVisible, Func<int> writeRoom) =>
        FloorPlanUpdateService.TryPersist(writeModel, modelVisible, writeRoom);

    internal static void ReturnConnectedClients(IReadOnlyList<GameClient> clients, System.Action<GameClient, bool> remove, System.Action reload, System.Action unload, System.Action<GameClient> forward) =>
        FloorPlanUpdateService.ReturnConnectedClients(clients, remove, reload, unload, forward);
}
