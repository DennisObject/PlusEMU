using Plus.Communication.Packets.Outgoing.Rooms.Notifications;
using Plus.Communication.Packets.Outgoing.Rooms.FloorPlan;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms;

public readonly record struct FloorPlanUpdateRequest(string Map, bool DoorFieldsPresent, bool WallHeightPresent, FloorPlanSave.Layout Requested);
public interface IFloorPlanUpdateService
{
    void Update(Room room, GameClient session, FloorPlanUpdateRequest body);
    void ShowEntryTile(GameClient session);
    void ShowOccupiedTiles(GameClient session);
}

public sealed class FloorPlanUpdateService : IFloorPlanUpdateService
{
    private readonly IRoomManager _roomManager;
    private readonly IFloorPlanStore _store;

    public FloorPlanUpdateService(IRoomManager roomManager, IFloorPlanStore store)
    {
        _roomManager = roomManager;
        _store = store;
    }

    public void ShowEntryTile(GameClient session)
    {
        var room = session.GetHabbo().CurrentRoom;
        var model = room?.GetGameMap().Model;

        if (model != null) {
            session.Send(new RoomEntryTileComposer(model.DoorX, model.DoorY, model.DoorOrientation));
        }
    }

    public void ShowOccupiedTiles(GameClient session)
    {
        var room = session.GetHabbo().CurrentRoom;

        if (room != null) {
            session.Send(new RoomOccupiedTilesComposer(FloorPlanSave.OccupiedTiles(FloorItems(room))));
        }
    }

    public void Update(Room room, GameClient session, FloorPlanUpdateRequest body)
    {
        if (session.GetHabbo().CurrentRoom != room || !room.CheckRights(session, true)) {
            return;
        }

        var model = room.GetGameMap().Model;

        if (model?.SqState == null || model.SqFloorHeight == null) {
            Notify(session, FloorPlanSave.ErrorTitle);

            return;
        }

        var existing = new FloorPlanSave.Layout(
            model.DoorX,
            model.DoorY,
            model.DoorOrientation,
            room.WallThickness,
            room.FloorThickness,
            room.GetGameMap().StaticModel.WallHeight);
        var layout = FloorPlanSave.Resolve(body.DoorFieldsPresent, body.WallHeightPresent, body.Requested, existing);

        if (Plus.HabboHotel.Subscriptions.ClubAccess.LevelFor(session.GetHabbo().Access) == 0 && (layout.WallThickness != 0 || layout.FloorThickness != 0)) {
            return;
        }

        var decision = FloorPlanSave.Evaluate(body.Map, layout.DoorX, layout.DoorY, layout.DoorDirection, layout.WallThickness, layout.FloorThickness, layout.WallHeight, FloorItems(room), CurrentTiles(model));

        if (decision.Error != null) {
            Notify(session, decision.Error);

            return;
        }

        var modelName = $"model_bc_{room.Id}";

        try {
            _store.Save(room.Id, modelName, decision);
        }
        catch (Exception) {
            Notify(session, FloorPlanSave.ErrorTitle);

            return;
        }

        room.ModelName = modelName;
        room.WallThickness = decision.WallThickness;
        room.FloorThickness = decision.FloorThickness;
        // Clear CurrentRoom while this manager is alive. UnloadRoom then disposes it.
        var userManager = room.GetRoomUserManager();
        var connectedClients = new List<GameClient>();

        foreach (var user in userManager.GetRoomUsers()) {
            var client = user?.GetClient();

            if (client == null) {
                continue;
            }

            connectedClients.Add(client);
        }

        var roomId = room.Id;
        ReturnConnectedClients(
            connectedClients,
            (client, notifyUser) => userManager.RemoveUserFromRoom(client, notifyUser),
            () => _roomManager.ReloadModel(modelName),
            () => _roomManager.UnloadRoom(roomId),
            client => client.Send(new RoomForwardComposer(roomId)));

        return;
    }

    internal static bool TryPersist(Func<int> writeModel, Func<bool> modelVisible, Func<int> writeRoom)
    {
        try {
            if (writeModel() < 1) {
                return false;
            }
        }
        catch (Exception) {
            return false;
        }

        if (!modelVisible()) {
            return false;
        }

        try {
            return writeRoom() > 0;
        }
        catch (Exception) {
            return false;
        }
    }

    internal static void ReturnConnectedClients(
        IReadOnlyList<GameClient> connectedClients,
        System.Action<GameClient, bool> removeFromRoom,
        System.Action reloadModel,
        System.Action unloadRoom,
        System.Action<GameClient> forward)
    {
        foreach (var client in connectedClients) {
            try {
                removeFromRoom(client, true);
            }
            finally {
                // RemoveUserFromRoom nulls CurrentRoom only after CloseConnectionComposer.
                // A failed send is swallowed and would leave the disposed room attached.
                ClearCurrentRoom(client);
            }
        }

        reloadModel();
        unloadRoom();

        foreach (var client in connectedClients) {
            forward(client);
        }
    }

    private static void ClearCurrentRoom(GameClient client)
    {
        Habbo? habbo = client.GetHabbo();

        if (habbo != null) {
            habbo.CurrentRoom = null;
        }
    }

    private static void Notify(GameClient session, string error) =>
        session.Send(new RoomNotificationComposer("floorplan_editor.error", "message", error));

    private static List<FloorPlanSave.FloorPlanItem> FloorItems(Room room)
    {
        var items = new List<FloorPlanSave.FloorPlanItem>();

        foreach (var item in room.GetRoomItemHandler().GetFloor) {
            if (item?.Definition == null) {
                continue;
            }

            var width = item.Definition.Width < 1 ? 1 : item.Definition.Width;
            var length = item.Definition.Length < 1 ? 1 : item.Definition.Length;
            items.Add(new FloorPlanSave.FloorPlanItem(item.Id, item.GetX, item.GetY, item.Rotation, width, length));
        }

        return items;
    }

    private static Dictionary<(int X, int Y), FloorPlanSave.FloorTile> CurrentTiles(DynamicRoomModel model)
    {
        var tiles = new Dictionary<(int X, int Y), FloorPlanSave.FloorTile>();
        var width = Math.Min(model.MapSizeX, model.SqState.GetLength(0));
        var height = Math.Min(model.MapSizeY, model.SqState.GetLength(1));

        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                tiles[(x, y)] = new FloorPlanSave.FloorTile(model.SqFloorHeight[x, y], model.SqState[x, y] != SquareState.Blocked);
            }
        }

        return tiles;
    }
}
