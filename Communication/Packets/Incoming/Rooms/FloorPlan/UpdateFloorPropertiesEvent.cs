using Plus.Communication.Packets.Outgoing.Rooms.Notifications;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Rooms.FloorPlan;

internal class UpdateFloorPropertiesEvent : RoomPacketEvent
{
    private readonly IRoomManager _roomManager;
    private readonly IDatabase _database;

    public UpdateFloorPropertiesEvent(IRoomManager roomManager, IDatabase database)
    {
        _roomManager = roomManager;
        _database = database;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session, true))
            return Task.CompletedTask;

        var model = room.GetGameMap().Model;
        if (model?.SqState == null || model.SqFloorHeight == null)
        {
            Notify(session, FloorPlanSave.ErrorTitle);
            return Task.CompletedTask;
        }

        var body = FloorPlanRequest.Read(packet);
        var existing = new FloorPlanSave.Layout(
            model.DoorX,
            model.DoorY,
            model.DoorOrientation,
            room.WallThickness,
            room.FloorThickness,
            room.GetGameMap().StaticModel.WallHeight);
        var layout = FloorPlanSave.Resolve(body.DoorFieldsPresent, body.WallHeightPresent, body.Requested, existing);

        var decision = FloorPlanSave.Evaluate(body.Map, layout.DoorX, layout.DoorY, layout.DoorDirection, layout.WallThickness, layout.FloorThickness, layout.WallHeight, FloorItems(room), CurrentTiles(model));
        if (decision.Error != null)
        {
            Notify(session, decision.Error);
            return Task.CompletedTask;
        }

        var modelName = $"model_bc_{room.Id}";
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.SetQuery("SELECT `id` FROM `room_models` WHERE `id` = @model AND `custom` = '1' LIMIT 1");
            dbClient.AddParameter("model", modelName);
            var row = dbClient.GetRow();
            if (row == null)
            {
                dbClient.SetQuery(
                    "INSERT INTO `room_models` (`id`,`door_x`,`door_y`, `door_z`, `door_dir`,`heightmap`,`public_items`,`custom`,`wall_height`) VALUES (@ModelName, @DoorX, @DoorY, @DoorZ, @DoorDirection, @Map, '', '1', @WallHeight)");
            }
            else
            {
                dbClient.SetQuery(
                    "UPDATE `room_models` SET `heightmap` = @Map, `door_x` = @DoorX, `door_y` = @DoorY, `door_z` = @DoorZ, `door_dir` = @DoorDirection, `wall_height` = @WallHeight WHERE `id` = @ModelName LIMIT 1");
            }

            dbClient.AddParameter("ModelName", modelName);
            dbClient.AddParameter("DoorX", decision.DoorX);
            dbClient.AddParameter("DoorY", decision.DoorY);
            dbClient.AddParameter("DoorZ", decision.DoorZ);
            dbClient.AddParameter("DoorDirection", decision.DoorDirection);
            dbClient.AddParameter("Map", decision.Map);
            dbClient.AddParameter("WallHeight", decision.WallHeight);
            bool persisted;
            try
            {
                persisted = dbClient.RunTransaction(() => TryPersist(
                    () => dbClient.RunQueryRequired(),
                    () =>
                    {
                        dbClient.SetQuery("SELECT `id` FROM `room_models` WHERE `id` = @model AND `custom` = '1' LIMIT 1");
                        dbClient.AddParameter("model", modelName);
                        return dbClient.GetRow() != null;
                    },
                    () =>
                    {
                        dbClient.SetQuery("UPDATE `rooms` SET `model_name` = @ModelName, `wallthick` = @WallThick, `floorthick` = @FloorThick WHERE `id` = @roomId LIMIT 1");
                        dbClient.AddParameter("roomId", room.Id);
                        dbClient.AddParameter("ModelName", modelName);
                        dbClient.AddParameter("WallThick", decision.WallThickness);
                        dbClient.AddParameter("FloorThick", decision.FloorThickness);
                        return dbClient.RunQueryRequired();
                    }));
            }
            catch (Exception)
            {
                Notify(session, FloorPlanSave.ErrorTitle);
                return Task.CompletedTask;
            }
            if (!persisted)
            {
                Notify(session, FloorPlanSave.ErrorTitle);
                return Task.CompletedTask;
            }
        }

        room.ModelName = modelName;
        room.WallThickness = decision.WallThickness;
        room.FloorThickness = decision.FloorThickness;
        // Clear CurrentRoom while this manager is alive. UnloadRoom then disposes it.
        var userManager = room.GetRoomUserManager();
        var connectedClients = new List<GameClient>();
        foreach (var user in userManager.GetRoomUsers())
        {
            var client = user?.GetClient();
            if (client == null)
                continue;
            connectedClients.Add(client);
        }

        var roomId = room.Id;
        ReturnConnectedClients(
            connectedClients,
            (client, notifyUser) => userManager.RemoveUserFromRoom(client, notifyUser),
            () => _roomManager.ReloadModel(modelName),
            () => _roomManager.UnloadRoom(roomId),
            client => client.Send(new RoomForwardComposer(roomId)));
        return Task.CompletedTask;
    }

    internal static bool TryPersist(Func<int> writeModel, Func<bool> modelVisible, Func<int> writeRoom)
    {
        try
        {
            if (writeModel() < 1)
                return false;
        }
        catch (Exception)
        {
            return false;
        }

        if (!modelVisible())
            return false;

        try
        {
            return writeRoom() > 0;
        }
        catch (Exception)
        {
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
        foreach (var client in connectedClients)
        {
            try
            {
                removeFromRoom(client, true);
            }
            finally
            {
                // RemoveUserFromRoom nulls CurrentRoom only after CloseConnectionComposer.
                // A failed send is swallowed and would leave the disposed room attached.
                ClearCurrentRoom(client);
            }
        }

        reloadModel();
        unloadRoom();

        foreach (var client in connectedClients)
            forward(client);
    }

    private static void ClearCurrentRoom(GameClient client)
    {
        Habbo? habbo = client.GetHabbo();
        if (habbo != null)
            habbo.CurrentRoom = null;
    }

    private static void Notify(GameClient session, string error) =>
        session.Send(new RoomNotificationComposer("floorplan_editor.error", "message", error));

    private static List<FloorPlanSave.FloorPlanItem> FloorItems(Room room)
    {
        var items = new List<FloorPlanSave.FloorPlanItem>();
        foreach (var item in room.GetRoomItemHandler().GetFloor)
        {
            if (item?.Definition == null)
                continue;
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
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                tiles[(x, y)] = new FloorPlanSave.FloorTile(model.SqFloorHeight[x, y], model.SqState[x, y] != SquareState.Blocked);
            }
        }
        return tiles;
    }
}
