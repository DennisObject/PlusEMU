using System.Collections.Concurrent;
using System.Data;
using Microsoft.Extensions.Logging;
using Plus.Core;
using Plus.Core.Language;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms;

public class RoomManager : IRoomManager
{
    private readonly ILogger<RoomManager> _logger;
    private readonly IDatabase _database;
    private readonly ILanguageManager _languageManager;

    private readonly object _roomLoadingSync;
    private readonly TimeProvider _clock;

    private readonly Dictionary<string, RoomModel> _roomModels;

    private readonly ConcurrentDictionary<uint, Room> _rooms;

    private DateTimeOffset _cycleLastExecution;
    private DateTimeOffset _wiredLastExecution;
    private readonly ConcurrentDictionary<uint, Room> _fastWiredRooms = new();


    public RoomManager(ILogger<RoomManager> logger, IDatabase database, ILanguageManager languageManager, TimeProvider clock)
    {
        _logger = logger;
        _database = database;
        _languageManager = languageManager;
        _clock = clock;
        _roomModels = new();
        _rooms = new();
        _roomLoadingSync = new();
    }

    public int Count => _rooms.Count;

    public void OnCycle()
    {
        try
        {
            var now = _clock.GetLocalNow();
            var fullPass = RoomCycle.IsDue(_cycleLastExecution, now);
            if (fullPass)
            {
                _cycleLastExecution = now;
                foreach (var room in _rooms.Values.ToList())
                {
                    if (room.IsCrashed)
                        continue;
                    var tick = RoomCycle.Next(room.ProcessTask is { IsCompleted: false }, room.IsLagging);
                    if (tick.Start)
                    {
                        RoomCycle.TryStart(room, room.ProcessRoom);
                        room.IsLagging = 0;
                    }
                    else
                    {
                        room.IsLagging = tick.Lag;
                        if (tick.Crashed)
                        {
                            room.IsCrashed = true;
                            UnloadRoom(room.Id);
                        }
                    }
                }
            }
            if (now - _wiredLastExecution >= RoomCycle.WiredInterval)
            {
                _wiredLastExecution = now;
                if (!fullPass && !_fastWiredRooms.IsEmpty)
                    foreach (var room in _fastWiredRooms.Values)
                        RoomCycle.TryStart(room, room.ProcessWiredOnly);
            }
        }
        catch (Exception e)
        {
            ExceptionLogger.LogException(e);
        }
    }

    public void LoadModels()
    {
        if (_roomModels.Count > 0)
            _roomModels.Clear();
        using var dbClient = _database.GetQueryReactor();
        dbClient.SetQuery("SELECT id,door_x,door_y,door_z,door_dir,heightmap,required_club_level,required_permission,poolmap,`wall_height` FROM `room_models` WHERE `custom` = '0'");
        var data = dbClient.GetTable();
        if (data == null)
            return;
        foreach (DataRow row in data.Rows)
        {
            var model = Convert.ToString(row["id"]);
            _roomModels.Add(model, new(model, Convert.ToInt32(row["door_x"]), Convert.ToInt32(row["door_y"]), (double)row["door_z"], Convert.ToInt32(row["door_dir"]),
                Convert.ToString(row["heightmap"]), Convert.ToInt32(row["required_club_level"]), Convert.ToInt32(row["wall_height"]), false)
            {
                RequiredPermission = row.IsNull("required_permission") ? null : Convert.ToString(row["required_permission"])
            });
        }
    }

    public bool LoadModel(string id)
    {
        DataRow row = null;
        using var dbClient = _database.GetQueryReactor();
        dbClient.SetQuery("SELECT id,door_x,door_y,door_z,door_dir,heightmap,required_club_level,required_permission,poolmap,`wall_height` FROM `room_models` WHERE `custom` = '1' AND `id` = @modelId LIMIT 1");
        dbClient.AddParameter("modelId", id);
        row = dbClient.GetRow();
        if (row == null)
            return false;
        var model = Convert.ToString(row["id"]);
        if (!_roomModels.ContainsKey(model))
        {
            _roomModels.Add(model, new(model, Convert.ToInt32(row["door_x"]), Convert.ToInt32(row["door_y"]), Convert.ToDouble(row["door_z"]), Convert.ToInt32(row["door_dir"]),
                Convert.ToString(row["heightmap"]), Convert.ToInt32(row["required_club_level"]), Convert.ToInt32(row["wall_height"]), true)
            {
                RequiredPermission = row.IsNull("required_permission") ? null : Convert.ToString(row["required_permission"])
            });
        }
        return true;
    }

    public void ReloadModel(string id)
    {
        if (!_roomModels.ContainsKey(id))
        {
            LoadModel(id);
            return;
        }
        _roomModels.Remove(id);
        LoadModel(id);
    }

    public bool TryGetModel(string id, out RoomModel model)
    {
        if (_roomModels.ContainsKey(id))
        {
            model = _roomModels[id];
            return true;
        }

        // Try to load this model.
        if (LoadModel(id))
        {
            if (TryGetModel(id, out var customModel))
            {
                model = customModel;
                return true;
            }
        }
        model = null;
        return false;
    }

    public void UnloadRoom(uint roomId)
    {
        if (_rooms.TryRemove(roomId, out var room))
        {
            room.GetWired().ObserveFastWork(null);
            _fastWiredRooms.TryRemove(roomId, out _);
            room.Dispose();
        }
    }

    public bool TryLoadRoom(uint roomId, out Room room)
    {
        Room inst = null;
        if (_rooms.TryGetValue(roomId, out inst))
        {
            if (!inst.Unloaded)
            {
                room = inst;
                return true;
            }
            room = null;
            return false;
        }
        lock (_roomLoadingSync)
        {
            if (_rooms.TryGetValue(roomId, out inst))
            {
                if (!inst.Unloaded)
                {
                    room = inst;
                    return true;
                }
                room = null;
                return false;
            }
            if (!RoomFactory.TryGetData(roomId, out var data))
            {
                room = null;
                return false;
            }
            var myInstance = new Room(data);
            if (_rooms.TryAdd(roomId, myInstance))
            {
                myInstance.GetWired().ObserveFastWork(required =>
                {
                    if (required && _rooms.TryGetValue(roomId, out var attached) && ReferenceEquals(attached, myInstance))
                        _fastWiredRooms[roomId] = myInstance;
                    else _fastWiredRooms.TryRemove(roomId, out _);
                });
                room = myInstance;
                return true;
            }
            room = null;
            return false;
        }
    }


    public List<Room> SearchGroupRooms(string query)
    {
        return _rooms.Values.Where(x => x.Group != null && x.Group.Name.ToLower().Contains(query.ToLower()) && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.UsersNow).Take(50).ToList();
    }

    public List<Room> SearchTaggedRooms(string query)
    {
        return _rooms.Values.Where(x => x.Tags.Contains(query) && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.UsersNow).Take(50).ToList();
    }

    public List<Room> GetPopularRooms(int category, int amount = 50)
    {
        return _rooms.Values.Where(x => x.UsersNow > 0 && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.UsersNow).Take(amount).ToList();
    }

    public List<Room> GetRecommendedRooms(int amount = 50, int currentRoomId = 0)
    {
        return _rooms.Values.Where(x => x.Id != currentRoomId && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.UsersNow).OrderByDescending(x => x.Score).Take(amount).ToList();
    }

    public List<Room> GetPopularRatedRooms(int amount = 50)
    {
        return _rooms.Values.Where(x => x.Access != RoomAccess.Invisible).OrderByDescending(x => x.Score).OrderByDescending(x => x.UsersNow).Take(amount).ToList();
    }

    public List<Room> GetRoomsByCategory(int category, int amount = 50)
    {
        return _rooms.Values.Where(x => x.Category == category && x.Access != RoomAccess.Invisible && x.UsersNow > 0).OrderByDescending(x => x.UsersNow).Take(amount).ToList();
    }

    public List<Room> GetOnGoingRoomPromotions(int mode, int amount = 50)
    {
        if (mode == 17) return _rooms.Values.Where(x => x.HasActivePromotion && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.Promotion.TimestampStarted).Take(amount).ToList();
        return _rooms.Values.Where(x => x.HasActivePromotion && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.UsersNow).Take(amount).ToList();
    }

    public List<Room> GetPromotedRooms(int categoryId, int amount = 50)
    {
        return _rooms.Values.Where(x => x.HasActivePromotion && x.Promotion.CategoryId == categoryId && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.Promotion.TimestampStarted)
            .Take(amount).ToList();
    }

    public List<Room> GetGroupRooms(int amount = 50)
    {
        return _rooms.Values.Where(x => x.Group != null && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.Score).Take(amount).ToList();
    }

    public List<Room> GetRoomsByIds(List<uint> ids, int amount = 50)
    {
        return _rooms.Values.Where(x => ids.Contains(x.Id) && x.Access != RoomAccess.Invisible).OrderByDescending(x => x.UsersNow).Take(amount).ToList();
    }

    public Room TryGetRandomLoadedRoom()
    {
        return _rooms.Values.Where(x => x.UsersNow > 0 && x.Access != RoomAccess.Invisible && x.UsersNow < x.UsersMax).OrderByDescending(x => x.UsersNow).FirstOrDefault();
    }


    public bool TryGetRoom(uint roomId, out Room room) => _rooms.TryGetValue(roomId, out room);

    public RoomData CreateRoom(GameClient session, string name, string description, int category, int maxVisitors, int tradeSettings, RoomModel model, string wallpaper = "0.0", string floor = "0.0",
        string landscape = "0.0", int wallthick = 0, int floorthick = 0)
    {
        if (name.Length < 3)
        {
            session.SendNotification(_languageManager.TryGetValue("room.creation.name.too_short"));
            return null;
        }
        var roomId = 0u;
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.SetQuery(
                "INSERT INTO `rooms` (`roomtype`,`caption`,`description`,`owner`,`model_name`,`category`,`users_max`,`trade_settings`) VALUES ('private',@caption,@description,@UserId,@model,@category,@usersmax,@tradesettings)");
            dbClient.AddParameter("caption", name);
            dbClient.AddParameter("description", description);
            dbClient.AddParameter("UserId", session.GetHabbo().Id);
            dbClient.AddParameter("model", model.Id);
            dbClient.AddParameter("category", category);
            dbClient.AddParameter("usersmax", maxVisitors);
            dbClient.AddParameter("tradesettings", tradeSettings);
            roomId = Convert.ToUInt32(dbClient.InsertQuery());
        }
        var data = new RoomData(roomId, name, model.Id, session.GetHabbo().Username, session.GetHabbo().Id, "", 0, "public", "open", 0, maxVisitors, category, description, string.Empty,
            floor, landscape, true, true, false, false, wallthick, floorthick, wallpaper, 1, 1, 1, 1, 1, 1, 1, 8, tradeSettings, true, true, true, true, true, true, true, 0, 0, true, model);
        return data;
    }

    public ICollection<Room> GetRooms() => _rooms.Values;

    public void Dispose()
    {
        var length = _rooms.Count;
        var i = 0;
        foreach (var room in _rooms.Values.ToList())
        {
            if (room == null)
                continue;
            UnloadRoom(room.Id);
            Console.Clear();
            _logger.LogInformation("<<- SERVER SHUTDOWN ->> ROOM ITEM SAVE: " + string.Format("{0:0.##}", (double)i / length * 100) + "%");
            i++;
        }
        _logger.LogInformation("Done disposing rooms!");
    }
}