using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Rooms;

public interface IFloorPlanStore
{
    void Save(uint roomId, string modelName, FloorPlanSave.Decision decision);
}

public sealed class FloorPlanStore(IDatabase database) : IFloorPlanStore
{
    public void Save(uint roomId, string modelName, FloorPlanSave.Decision decision)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var custom = connection.QuerySingleOrDefault<bool?>("SELECT custom FROM room_models WHERE id=@modelName LIMIT 1", new
        {
            modelName
        }, transaction);
        int written;

        if (custom == null)
        {
            written = connection.Execute("INSERT INTO room_models (id,door_x,door_y,door_z,door_dir,heightmap,public_items,custom,wall_height) VALUES (@modelName,@DoorX,@DoorY,@DoorZ,@DoorDirection,@Map,'',TRUE,@WallHeight)",
                new
                {
                    modelName,
                    decision.DoorX,
                    decision.DoorY,
                    decision.DoorZ,
                    decision.DoorDirection,
                    decision.Map,
                    decision.WallHeight
                }, transaction);
        }
        else if (custom.Value)
        {
            written = connection.Execute("UPDATE room_models SET heightmap=@Map,door_x=@DoorX,door_y=@DoorY,door_z=@DoorZ,door_dir=@DoorDirection,wall_height=@WallHeight WHERE id=@modelName AND custom=TRUE LIMIT 1",
                new
                {
                    modelName,
                    decision.DoorX,
                    decision.DoorY,
                    decision.DoorZ,
                    decision.DoorDirection,
                    decision.Map,
                    decision.WallHeight
                }, transaction);
        }
        else
        {
            throw new InvalidOperationException("A non-custom room model uses the requested identifier.");
        }

        if (written != 1 || connection.Execute("UPDATE rooms SET model_name=@modelName,wallthick=@WallThickness,floorthick=@FloorThickness WHERE id=@roomId LIMIT 1",
                new
                {
                    roomId,
                    modelName,
                    decision.WallThickness,
                    decision.FloorThickness
                }, transaction) != 1)
        {
            throw new InvalidOperationException("Floor plan was not persisted.");
        }

        transaction.Commit();
    }
}
