using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using Dapper;
using Microsoft.Extensions.Logging;
using Plus.Core;
using Plus.Core.Language;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms;

public class RoomManager : IRoomManager, IStartable
{
    private readonly ILogger<RoomManager> _logger;
    private readonly IDatabase _database;
    private readonly ILanguageManager _languageManager;

    private readonly object _roomLoadingSync;
    private readonly TimeProvider _clock;
    private readonly IRoomFactory _roomFactory;

    private readonly Dictionary<string, RoomModel> _roomModels;

    private readonly ConcurrentDictionary<uint, Room> _rooms;

    private DateTimeOffset _cycleLastExecution;
    private DateTimeOffset _wiredLastExecution;
    private readonly ConcurrentDictionary<uint, Room> _fastWiredRooms = new();


    public RoomManager(ILogger<RoomManager> logger, IDatabase database, ILanguageManager languageManager, TimeProvider clock, IRoomFactory roomFactory)
    {
        _logger = logger;
        _database = database;
        _languageManager = languageManager;
        _clock = clock;
        _roomFactory = roomFactory;
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

    private const string SelectModel = "SELECT id, door_x AS DoorX, door_y AS DoorY, door_z AS DoorZ, door_dir AS DoorDir, " +
        "heightmap, required_club_level AS RequiredClubLevel, required_permission AS RequiredPermission, wall_height AS WallHeight FROM room_models ";

    public int StartOrder => 20;
    public Task Start() => LoadModelsAsync();

    public void LoadModels() => LoadModelsAsync().GetAwaiter().GetResult();

    private async Task LoadModelsAsync()
    {
        using var connection = _database.Connection();
        var models = await connection.QueryAsync<ModelRow>(SelectModel + "WHERE custom = FALSE");
        _roomModels.Clear();
        foreach (var row in models)
            _roomModels.Add(row.Id, CreateModel(row, false));
    }

    public IReadOnlyList<RoomModel> GetCreatableModels(Plus.HabboHotel.Permissions.UserAccess access) =>
        _roomModels.Values.Where(model => !model.IsCustom && model.CanCreate(access)).OrderBy(model => model.Id, StringComparer.Ordinal).ToArray();

    public bool LoadModel(string id)
    {
        using var connection = _database.Connection();
        var row = connection.QuerySingleOrDefault<ModelRow>(SelectModel + "WHERE custom = TRUE AND id = @id LIMIT 1", new { id });
        if (row == null)
            return false;
        _roomModels.TryAdd(row.Id, CreateModel(row, true));
        return true;
    }

    private static RoomModel CreateModel(ModelRow row, bool custom) => new(row.Id, row.DoorX, row.DoorY, row.DoorZ,
        row.DoorDir, row.Heightmap, row.RequiredClubLevel, row.WallHeight, custom) { RequiredPermission = row.RequiredPermission };

    private sealed class ModelRow
    {
        public string Id { get; set; } = string.Empty;
        public int DoorX { get; set; }
        public int DoorY { get; set; }
        public double DoorZ { get; set; }
        public int DoorDir { get; set; }
        public string Heightmap { get; set; } = string.Empty;
        public int RequiredClubLevel { get; set; }
        public string? RequiredPermission { get; set; }
        public int WallHeight { get; set; }
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

    public bool TryGetModel(string id, [NotNullWhen(true)] out RoomModel? model)
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
            _roomFactory.Dispose(roomId);
        }
    }

    public bool TryLoadRoom(uint roomId, [NotNullWhen(true)] out Room? room)
    {
        Room? inst = null;
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
            var myInstance = _roomFactory.Create(data);
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
            myInstance.Dispose();
            _roomFactory.Dispose(roomId);
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

    public Room? TryGetRandomLoadedRoom()
    {
        return _rooms.Values.Where(x => x.UsersNow > 0 && x.Access != RoomAccess.Invisible && x.UsersNow < x.UsersMax).OrderByDescending(x => x.UsersNow).FirstOrDefault();
    }


    public bool TryGetRoom(uint roomId, [NotNullWhen(true)] out Room? room) => _rooms.TryGetValue(roomId, out room);

    public RoomData? CreateRoom(GameClient session, string name, string description, int category, int maxVisitors, int tradeSettings, RoomModel model, string wallpaper = "0.0", string floor = "0.0",
        string landscape = "0.0", int wallthick = 0, int floorthick = 0)
    {
        if (name.Length < 3)
        {
            session.SendNotification(_languageManager.TryGetValue("room.creation.name.too_short"));
            return null;
        }
        using var connection = _database.Connection();
        var roomId = connection.QuerySingle<uint>(
            "INSERT INTO `rooms` (`roomtype`,`caption`,`description`,`owner`,`model_name`,`category`,`users_max`,`trade_settings`) " +
            "VALUES ('private',@name,@description,@ownerId,@modelId,@category,@maxVisitors,@tradeSettings); SELECT LAST_INSERT_ID()",
            new { name, description, ownerId = session.GetHabbo().Id, modelId = model.Id, category, maxVisitors, tradeSettings });
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
