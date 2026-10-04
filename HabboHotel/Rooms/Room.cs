using System.Data;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.Core;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Data.Moodlight;
using Plus.HabboHotel.Items.Data.Toner;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Speech;
using Plus.HabboHotel.Rooms.Games;
using Plus.HabboHotel.Rooms.Games.Banzai;
using Plus.HabboHotel.Rooms.Games.Football;
using Plus.HabboHotel.Rooms.Games.Freeze;
using Plus.HabboHotel.Rooms.Games.Teams;
using Plus.HabboHotel.Rooms.Instance;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms;

public class Room : RoomData
{
    private readonly BansComponent _bansComponent;

    private readonly FilterComponent _filterComponent;

    private readonly Dictionary<uint, List<RoomUser>> _tents;
    private readonly TradingComponent _tradingComponent;
    private readonly WiredComponent _wiredComponent;
    private BattleBanzai _banzai;
    private Freeze _freeze;
    private GameItemHandler _gameItemHandler;
    private GameManager _gameManager;

    private Gamemap _gamemap;
    private RoomItemHandling _roomItemHandling;

    private RoomUserManager _roomUserManager;
    private Soccer _soccer;

    public bool IsCrashed;
    public DateTime LastRegeneration;
    public DateTime LastTimerReset;
    public bool MDisposed;
    public MoodlightData MoodlightData;

    public Dictionary<int, double> MutedUsers;

    public Task ProcessTask;
    private bool _usesV2Movement;
    internal bool UsesV2Movement => _usesV2Movement;
    internal void EnableV2Movement() => _usesV2Movement = true;
    private object? _navigationSync;
    internal object NavigationSync => LazyInitializer.EnsureInitialized(ref _navigationSync);
    public bool RoomMuted;

    public TeamManager Teambanzai;
    public TeamManager Teamfreeze;

    public TonerData TonerData;

    public List<int> UsersWithRights;

    public Room(RoomData data)
        : base(data)
    {
        IsLagging = 0;
        Unloaded = false;
        IdleTime = 0;
        RoomMuted = false;
        MutedUsers = new();
        _tents = new();
        _gamemap = new(this, data.Model);
        _roomItemHandling = new(this);
        _roomUserManager = new(this);
        _filterComponent = new(this);
        _wiredComponent = new(this);
        _bansComponent = new(this);
        _tradingComponent = new(this);
        GetRoomItemHandler().LoadFurniture();
        GetGameMap().GenerateMaps();
        LoadPromotions();
        LoadRights();
        LoadFilter();
        InitBots();
        InitPets();
        LastRegeneration = DateTime.Now;
    }

    public int IsLagging { get; set; }
    public bool Unloaded { get; set; }
    public int IdleTime { get; set; }

    public List<string> WordFilterList { get; set; }

    public int UserCount => _roomUserManager.GetRoomUsers().Count;

    public uint RoomId => Id;

    public bool CanTradeInRoom => true;

    public Gamemap GetGameMap() => _gamemap;

    public RoomItemHandling GetRoomItemHandler()
    {
        if (_roomItemHandling == null) _roomItemHandling = new(this);
        return _roomItemHandling;
    }

    public RoomUserManager GetRoomUserManager() => _roomUserManager;

    public Soccer GetSoccer()
    {
        if (_soccer == null)
            _soccer = new(this);
        return _soccer;
    }

    public TeamManager GetTeamManagerForBanzai()
    {
        if (Teambanzai == null)
            Teambanzai = TeamManager.CreateTeam("banzai");
        return Teambanzai;
    }

    public TeamManager GetTeamManagerForFreeze()
    {
        if (Teamfreeze == null)
            Teamfreeze = TeamManager.CreateTeam("freeze");
        return Teamfreeze;
    }

    public BattleBanzai GetBanzai()
    {
        if (_banzai == null)
            _banzai = new(this);
        return _banzai;
    }

    public Freeze GetFreeze()
    {
        if (_freeze == null)
            _freeze = new(this);
        return _freeze;
    }

    public GameManager GetGameManager()
    {
        if (_gameManager == null)
            _gameManager = new(this);
        return _gameManager;
    }

    public GameItemHandler GetGameItemHandler()
    {
        if (_gameItemHandler == null)
            _gameItemHandler = new(this);
        return _gameItemHandler;
    }

    public bool GotSoccer() => _soccer != null;

    public bool GotBanzai() => _banzai != null;

    public bool GotFreeze() => _freeze != null;

    public void ClearTags()
    {
        Tags.Clear();
    }

    public void AddTagRange(List<string> tags)
    {
        Tags.AddRange(tags);
    }

    public void InitBots()
    {
        using var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor();
        dbClient.SetQuery(
            $"SELECT `id`,`room_id`,`name`,`motto`,`look`,`x`,`y`,`z`,`rotation`,`gender`,`user_id`,`ai_type`,`walk_mode`,`automatic_chat`,`speaking_interval`,`mix_sentences`,`chat_bubble` FROM `bots` WHERE `room_id` = '{RoomId}' AND `ai_type` != 'pet'");
        var data = dbClient.GetTable();
        if (data == null)
            return;
        foreach (DataRow bot in data.Rows)
        {
            dbClient.SetQuery($"SELECT `text` FROM `bots_speech` WHERE `bot_id` = '{Convert.ToInt32(bot["id"])}'");
            var botSpeech = dbClient.GetTable();
            var speeches = new List<RandomSpeech>();
            foreach (DataRow speech in botSpeech.Rows) speeches.Add(new(Convert.ToString(speech["text"]), Convert.ToInt32(bot["id"])));
            _roomUserManager.DeployBot(
                new(Convert.ToInt32(bot["id"]), Convert.ToUInt32(bot["room_id"]), Convert.ToString(bot["ai_type"]), Convert.ToString(bot["walk_mode"]), Convert.ToString(bot["name"]),
                    Convert.ToString(bot["motto"]), Convert.ToString(bot["look"]), int.Parse(bot["x"].ToString()), int.Parse(bot["y"].ToString()), int.Parse(bot["z"].ToString()),
                    int.Parse(bot["rotation"].ToString()), 0, 0, 0, 0, ref speeches, "M", 0, Convert.ToInt32(bot["user_id"].ToString()), Convert.ToBoolean(bot["automatic_chat"]),
                    Convert.ToInt32(bot["speaking_interval"]), ConvertExtensions.EnumToBool(bot["mix_sentences"].ToString()), Convert.ToInt32(bot["chat_bubble"])), null);
        }
    }

    public void InitPets()
    {
        using var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor();
        dbClient.SetQuery($"SELECT `id`,`user_id`,`room_id`,`name`,`x`,`y`,`z` FROM `bots` WHERE `room_id` = '{RoomId}' AND `ai_type` = 'pet'");
        var data = dbClient.GetTable();
        if (data == null)
            return;
        foreach (DataRow row in data.Rows)
        {
            dbClient.SetQuery(
                $"SELECT `type`,`race`,`color`,`experience`,`energy`,`nutrition`,`respect`,`createstamp`,`have_saddle`,`anyone_ride`,`hairdye`,`pethair`,`gnome_clothing` FROM `bots_petdata` WHERE `id` = '{row[0]}' LIMIT 1");
            var mRow = dbClient.GetRow();
            if (mRow == null)
                continue;
            var pet = new Pet(Convert.ToInt32(row["id"]), Convert.ToInt32(row["user_id"]), Convert.ToUInt32(row["room_id"]), Convert.ToString(row["name"]), Convert.ToInt32(mRow["type"]),
                Convert.ToString(mRow["race"]),
                Convert.ToString(mRow["color"]), Convert.ToInt32(mRow["experience"]), Convert.ToInt32(mRow["energy"]), Convert.ToInt32(mRow["nutrition"]), Convert.ToInt32(mRow["respect"]),
                Convert.ToDouble(mRow["createstamp"]), Convert.ToInt32(row["x"]), Convert.ToInt32(row["y"]),
                Convert.ToDouble(row["z"]), Convert.ToInt32(mRow["have_saddle"]), Convert.ToInt32(mRow["anyone_ride"]), Convert.ToInt32(mRow["hairdye"]), Convert.ToInt32(mRow["pethair"]),
                Convert.ToString(mRow["gnome_clothing"]));
            var rndSpeechList = new List<RandomSpeech>();
            _roomUserManager.DeployBot(
                new(pet.PetId, RoomId, "pet", "freeroam", pet.Name, "", pet.Look, pet.X, pet.Y, Convert.ToInt32(pet.Z), 0, 0, 0, 0, 0, ref rndSpeechList, "", 0, pet.OwnerId, false, 0, false,
                    0), pet);
        }
    }

    public FilterComponent GetFilter() => _filterComponent;

    public WiredComponent GetWired() => _wiredComponent;

    public BansComponent GetBans() => _bansComponent;

    public TradingComponent GetTrading() => _tradingComponent;

    public void LoadRights()
    {
        UsersWithRights = new();
        if (Group != null)
            return;
        DataTable data = null;
        using (var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor())
        {
            dbClient.SetQuery("SELECT room_rights.user_id FROM room_rights WHERE room_id = @roomid");
            dbClient.AddParameter("roomid", Id);
            data = dbClient.GetTable();
        }
        if (data != null)
            foreach (DataRow row in data.Rows)
                UsersWithRights.Add(Convert.ToInt32(row["user_id"]));
    }

    private void LoadFilter()
    {
        WordFilterList = new();
        DataTable data = null;
        using (var dbClient = PlusEnvironment.DatabaseManager.GetQueryReactor())
        {
            dbClient.SetQuery("SELECT * FROM `room_filter` WHERE `room_id` = @roomid;");
            dbClient.AddParameter("roomid", Id);
            data = dbClient.GetTable();
        }
        if (data == null)
            return;
        foreach (DataRow row in data.Rows) WordFilterList.Add(Convert.ToString(row["word"]));
    }

    public bool CheckRights(GameClient session) => CheckRights(session, false);

    public bool CheckRights(GameClient session, bool requireOwnership, bool checkForGroups = false)
    {
        try
        {
            if (session == null || session.GetHabbo() == null)
                return false;
            if (session.GetHabbo().Username == OwnerName && Type == "private")
                return true;
            if (session.GetHabbo().Permissions.HasRight("room_any_owner"))
                return true;
            if (!requireOwnership && Type == "private")
            {
                if (session.GetHabbo().Permissions.HasRight("room_any_rights"))
                    return true;
                if (UsersWithRights.Contains(session.GetHabbo().Id))
                    return true;
            }
            if (checkForGroups && Type == "private")
            {
                if (Group == null)
                    return false;
                if (Group.IsAdmin(session.GetHabbo().Id))
                    return true;
                if (Group.AdminOnlyDeco == 0)
                {
                    if (Group.IsAdmin(session.GetHabbo().Id))
                        return true;
                }
            }
        }
        catch (Exception e)
        {
            ExceptionLogger.LogException(e);
        }
        return false;
    }

    public void OnUserShoot(RoomUser user, Item ball)
    {
        Func<Item, bool> predicate = null;
        string key = null;
        foreach (var item in GetRoomItemHandler().GetFurniObjects(ball.GetX, ball.GetY).ToList())
        {
            if (item.Definition.ItemName.StartsWith("fball_goal_"))
            {
                key = item.Definition.ItemName.Split(new[] { '_' })[2];
                user.UnIdle();
                user.DanceId = 0;
                PlusEnvironment.Game.AchievementManager.ProgressAchievement(user.GetClient(), "ACH_FootballGoalScored", 1);
                SendPacket(new ActionComposer(user.VirtualId, 1));
            }
        }
        if (key != null)
        {
            if (predicate == null) predicate = p => p.Definition.ItemName == $"fball_score_{key}";
            foreach (var item2 in GetRoomItemHandler().GetFloor.Where(predicate).ToList())
            {
                if (item2.Definition.ItemName == $"fball_score_{key}")
                {
                    if (!string.IsNullOrEmpty(item2.LegacyDataString))
                        item2.LegacyDataString = (Convert.ToInt32(item2.LegacyDataString) + 1).ToString();
                    else
                        item2.LegacyDataString = "1";
                    item2.UpdateState();
                }
            }
        }
    }

    internal void ProcessWiredOnly()
    {
        if (UsesV2Movement)
        {
            lock (NavigationSync) ProcessWiredOwned();
            return;
        }
        if (IsCrashed || MDisposed) return;
        try { GetWired().OnFastCycle(); }
        catch (Exception e) { ExceptionLogger.LogException(e); }
    }

    // v2 fast pass: serialized with ProcessRoom (one process task at a time), so it owns the room just like a
    // full tick. Legacy and shadow rooms keep the original pass, with no owner scope.
    internal void RunFastPass(Action pass)
    {
        if (UsesV2Movement)
        {
            lock (NavigationSync) RunOwnedPass(pass);
            return;
        }
        if (IsCrashed || MDisposed) return;
        try { pass(); }
        catch (Exception e) { ExceptionLogger.LogException(e); }
    }

    public void ProcessRoom()
    {
        if (UsesV2Movement)
        {
            lock (NavigationSync) ProcessRoomOwned();
            return;
        }
        if (IsCrashed || MDisposed)
            return;
        try
        {
            if (GetRoomUserManager().GetRoomUsers().Count == 0)
                IdleTime++;
            else if (IdleTime > 0)
                IdleTime = 0;
            if (HasActivePromotion && Promotion.HasExpired) EndPromotion();
            if (IdleTime >= 60 && !HasActivePromotion)
            {
                PlusEnvironment.Game.RoomManager.UnloadRoom(Id);
                return;
            }
            try
            {
                GetGameMap().Navigation?.ApplyDirty();
                GetRoomItemHandler().OnCycle();
            }
            catch (Exception e)
            {
                ExceptionLogger.LogException(e);
            }
            try
            {
                GetGameMap().Navigation?.ApplyDirty();
                GetRoomUserManager().OnCycle();
            }
            catch (Exception e)
            {
                ExceptionLogger.LogException(e);
            }
            try
            {
                GetRoomUserManager().SerializeStatusUpdates();
            }
            catch (Exception e)
            {
                ExceptionLogger.LogException(e);
            }
            try
            {
                if (_gameItemHandler != null)
                    _gameItemHandler.OnCycle();
            }
            catch (Exception e)
            {
                ExceptionLogger.LogException(e);
            }
            try
            {
                GetWired().OnCycle();
                GetGameMap().FlushPlacementUpdates();
            }
            catch (Exception e)
            {
                ExceptionLogger.LogException(e);
            }
        }
        catch (Exception e)
        {
            ExceptionLogger.LogException(e);
            OnRoomCrash(e);
        }
    }

    private void ProcessRoomOwned()
    {
        if (IsCrashed || MDisposed) return;
        using var owner = Plus.HabboHotel.Rooms.PathFinding.RoomOwnerScope.Enter(this);
        try
        {
            if (!KeepRoomActive()) return;
            RunRoomPhase(CycleFurniture);
            RunRoomPhase(CycleActors);
            RunRoomPhase(() => GetRoomUserManager().SerializeStatusUpdates());
            RunRoomPhase(() => _gameItemHandler?.OnCycle());
            RunRoomPhase(CycleWired);
        }
        catch (Exception error) { ExceptionLogger.LogException(error); OnRoomCrash(error); }
    }

    private bool KeepRoomActive()
    {
        if (GetRoomUserManager().GetRoomUsers().Count == 0) IdleTime++;
        else if (IdleTime > 0) IdleTime = 0;
        if (HasActivePromotion && Promotion.HasExpired) EndPromotion();
        if (IdleTime < 60 || HasActivePromotion) return true;
        PlusEnvironment.Game.RoomManager.UnloadRoom(Id);
        return false;
    }

    private void RunRoomPhase(Action phase)
    {
        if (MDisposed) return;
        try { phase(); }
        catch (Exception error) { ExceptionLogger.LogException(error); }
    }

    private void CycleFurniture()
    {
        GetGameMap().Navigation?.ApplyDirty();
        GetGameMap().Navigation?.DrainCommands();
        if (UsesV2Movement) GetGameMap().Gates.Drain();
        GetRoomItemHandler().OnCycle();
    }

    private void CycleActors()
    {
        GetGameMap().Navigation?.ApplyDirty();
        GetGameMap().Navigation?.DrainCommands();
        GetRoomUserManager().OnCycle();
    }

    private void CycleWired()
    {
        GetWired().OnCycle();
        GetGameMap().FlushPlacementUpdates();
    }

    private void OnRoomCrash(Exception e)
    {
        try
        {
            foreach (var user in _roomUserManager.GetRoomUsers().ToList())
            {
                if (user == null || user.GetClient() == null)
                    continue;
                user.GetClient().SendNotification("Sorry, it appears that room has crashed!"); //Unhandled exception in room: " + e);
                try
                {
                    GetRoomUserManager().RemoveUserFromRoom(user.GetClient(), true);
                }
                catch (Exception e2)
                {
                    ExceptionLogger.LogException(e2);
                }
            }
        }
        catch (Exception e3)
        {
            ExceptionLogger.LogException(e3);
        }
        IsCrashed = true;
        PlusEnvironment.Game.RoomManager.UnloadRoom(Id);
    }


    public bool CheckMute(GameClient session)
    {
        if (MutedUsers.ContainsKey(session.GetHabbo().Id))
        {
            if (MutedUsers[session.GetHabbo().Id] < UnixTimestamp.GetNow())
                MutedUsers.Remove(session.GetHabbo().Id);
            else
                return true;
        }
        if (session.GetHabbo().TimeMuted > 0 || RoomMuted && session.GetHabbo().Username != OwnerName)
            return true;
        return false;
    }

    public void SendObjects(GameClient session)
    {
        GetGameMap().SendPlacementHeightMap(session);
        session.Send(new FloorHeightMapComposer(GetGameMap().Model.GetRelativeHeightmap(), GetGameMap().StaticModel.WallHeight));
        var snapshotUsers = _roomUserManager.GetUserList().Where(user => user != null).ToArray();
        foreach (var user in snapshotUsers)
        {
            if (user == null)
                continue;
            session.Send(new UsersComposer(user));
            if (user.IsBot && user.BotData.DanceId > 0)
                session.Send(new DanceComposer(user, user.BotData.DanceId));
            else if (!user.IsBot && !user.IsPet && user.IsDancing)
                session.Send(new DanceComposer(user, user.DanceId));
            if (user.IsAsleep)
                session.Send(new SleepComposer(user, true));
            if (user.CarryItemId > 0 && user.CarryTimer > 0)
                session.Send(new CarryObjectComposer(user.VirtualId, user.CarryItemId));
            if (!user.IsBot && !user.IsPet && user.CurrentEffect > 0)
                session.Send(new AvatarEffectComposer(user.VirtualId, user.CurrentEffect));
        }
        session.Send(new UserUpdateComposer(_roomUserManager.GetUserList().ToList()));
        var snapshotFurniture = GetRoomItemHandler().GetFloor.ToArray();
        session.Send(new ObjectsComposer(snapshotFurniture, this));
        var snapshotWalls = GetRoomItemHandler().GetWall.ToArray();
        session.Send(new ItemsComposer(snapshotWalls, this));
        _wiredComponent?.SnapshotEnqueued(session, snapshotFurniture.Concat(snapshotWalls), snapshotUsers);
    }

    public void AddTent(uint tentId)
    {
        if (_tents.ContainsKey(tentId))
            _tents.Remove(tentId);
        _tents.Add(tentId, new());
    }

    public void RemoveTent(uint tentId)
    {
        if (!_tents.ContainsKey(tentId))
            return;
        var users = _tents[tentId];
        foreach (var user in users.ToList())
        {
            if (user == null || user.GetClient() == null || user.GetClient().GetHabbo() == null)
                continue;
            user.GetClient().GetHabbo().TentId = 0;
        }
        if (_tents.ContainsKey(tentId))
            _tents.Remove(tentId);
    }

    public void AddUserToTent(uint tentId, RoomUser user)
    {
        if (user != null && user.GetClient() != null && user.GetClient().GetHabbo() != null)
        {
            if (!_tents.ContainsKey(tentId))
                _tents.Add(tentId, new());
            if (!_tents[tentId].Contains(user))
                _tents[tentId].Add(user);
            user.GetClient().GetHabbo().TentId = tentId;
        }
    }

    public void RemoveUserFromTent(uint tentId, RoomUser user)
    {
        if (user != null && user.GetClient() != null && user.GetClient().GetHabbo() != null)
        {
            if (!_tents.ContainsKey(tentId))
                _tents.Add(tentId, new());
            if (_tents[tentId].Contains(user))
                _tents[tentId].Remove(user);
            user.GetClient().GetHabbo().TentId = 0;
        }
    }

    public void SendToTent(int id, uint tentId, IServerPacket packet)
    {
        if (!_tents.ContainsKey(tentId))
            return;
        foreach (var user in _tents[tentId].ToList())
        {
            if (user == null || user.GetClient() == null || user.GetClient().GetHabbo() == null || user.GetClient().GetHabbo().IgnoresComponent.IsIgnored(id) ||
                user.GetClient().GetHabbo().TentId != tentId)
                continue;
            user.GetClient().Send(packet);
        }
    }

    public void SendPacket(IServerPacket packet, bool withRightsOnly = false) => SendPacket(packet, withRightsOnly, null);

    public void SendObject(Item item) => SendPacket(item.IsWallItem ? new ItemAddComposer(item) : new ObjectAddComposer(item), false,
        viewer => _wiredComponent?.ObjectEnqueued(viewer, item, null));

    public void SendUser(RoomUser user) => SendPacket(new UsersComposer(user), false,
        viewer => _wiredComponent?.ObjectEnqueued(viewer, null, user));

    private void SendPacket(IServerPacket packet, bool withRightsOnly, Action<RoomUser>? enqueued)
    {
        if (packet == null)
            return;
        try
        {
            if (_roomUserManager == null)
                return;
            GameClient.SendBroadcast(packet, GetRecipients());

            IEnumerable<GameClient> GetRecipients()
            {
                foreach (var user in _roomUserManager.GetRoomUsers())
                {
                    var client = user?.GetClient();
                    if (client == null || withRightsOnly && !CheckRights(client))
                        continue;
                    yield return client;
                    // SendBroadcast sends inside its loop, so this runs after the packet is queued for this viewer.
                    enqueued?.Invoke(user);
                }
            }
        }
        catch (Exception e)
        {
            ExceptionLogger.LogException(e);
        }
    }

    public void SendPacket(List<IServerPacket> packets)
    {
        foreach (var packet in packets)
            SendPacket(packet);
    }

    public void Dispose()
    {
        if (UsesV2Movement)
        {
            DisposeExecutorRoom();
            return;
        }
        if (MDisposed)
            return;
        IsCrashed = false;
        MDisposed = true;
        _gamemap?.ClosePlacementUpdates();
        // Drop every user before the managers are destroyed. A habbo left
        // pointing at this room makes the next enter throw and disconnect.
        if (_roomUserManager != null)
        {
            foreach (var user in _roomUserManager.GetRoomUsers().ToList())
            {
                var client = user?.GetClient();
                if (client == null)
                    continue;
                _roomUserManager.RemoveUserFromRoom(client, true);
            }
        }
        /* TODO: Needs reviewing */
        try
        {
            if (ProcessTask != null && ProcessTask.IsCompleted)
                ProcessTask.Dispose();
        }
        catch { }
        TonerData = null;
        MoodlightData = null;
        if (MutedUsers.Count > 0)
            MutedUsers.Clear();
        if (_tents.Count > 0)
            _tents.Clear();
        if (UsersWithRights.Count > 0)
            UsersWithRights.Clear();
        if (_gameManager != null)
        {
            _gameManager.Dispose();
            _gameManager = null;
        }
        if (_freeze != null)
        {
            _freeze.Dispose();
            _freeze = null;
        }
        if (_soccer != null)
        {
            _soccer.Dispose();
            _soccer = null;
        }
        if (_banzai != null)
        {
            _banzai.Dispose();
            _banzai = null;
        }
        if (_gamemap != null)
        {
            _gamemap.Dispose();
            _gamemap = null;
        }
        if (_gameItemHandler != null)
        {
            _gameItemHandler.Dispose();
            _gameItemHandler = null;
        }

        // Room Data?
        if (Teambanzai != null)
        {
            Teambanzai.Dispose();
            Teambanzai = null;
        }
        if (Teamfreeze != null)
        {
            Teamfreeze.Dispose();
            Teamfreeze = null;
        }
        if (_roomUserManager != null)
        {
            _roomUserManager.Dispose();
            _roomUserManager = null;
        }
        if (_roomItemHandling != null)
        {
            _roomItemHandling.Dispose();
            _roomItemHandling = null;
        }
        if (WordFilterList.Count > 0)
            WordFilterList.Clear();
        if (_filterComponent != null)
            _filterComponent.Cleanup();
        if (_wiredComponent != null)
            _wiredComponent.Cleanup();
        if (_bansComponent != null)
            _bansComponent.Cleanup();
        if (_tradingComponent != null)
            _tradingComponent.Cleanup();
    }

    private void ProcessWiredOwned() => RunOwnedPass(() => GetWired().OnFastCycle());

    // The fast pass owns the room for the gate sequencer, so Wired closes run inline and in order.
    private void RunOwnedPass(Action pass)
    {
        if (IsCrashed || MDisposed) return;
        using var owner = Plus.HabboHotel.Rooms.PathFinding.RoomOwnerScope.Enter(this);
        try { pass(); }
        catch (Exception e) { ExceptionLogger.LogException(e); }
    }

    private void DisposeExecutorRoom()
    {
        lock (NavigationSync)
        {
            if (MDisposed) return;
            IsCrashed = false; MDisposed = true;
            _gamemap?.Navigation?.Shutdown();
            _gamemap?.ClosePlacementUpdates();
            RemoveRemainingUsers();
            DisposeCompletedTask();
            ClearRoomCollections();
            DisposeGames();
            DisposeMapAndTeams();
            DisposeRoomManagers();
            ClearRoomComponents();
        }
    }

    private void RemoveRemainingUsers()
    {
        if (_roomUserManager == null) return;
        foreach (var user in _roomUserManager.GetRoomUsers().ToList())
        {
            var client = user?.GetClient();
            if (client != null) _roomUserManager.RemoveUserFromRoom(client, true);
        }
    }

    private void DisposeCompletedTask()
    {
        try { if (ProcessTask is { IsCompleted: true }) ProcessTask.Dispose(); }
        catch { }
    }

    private void ClearRoomCollections()
    {
        TonerData = null; MoodlightData = null;
        if (MutedUsers.Count > 0) MutedUsers.Clear();
        if (_tents.Count > 0) _tents.Clear();
        if (UsersWithRights.Count > 0) UsersWithRights.Clear();
    }

    private void DisposeGames()
    {
        if (_gameManager != null) { _gameManager.Dispose(); _gameManager = null; }
        if (_freeze != null) { _freeze.Dispose(); _freeze = null; }
        if (_soccer != null) { _soccer.Dispose(); _soccer = null; }
        if (_banzai != null) { _banzai.Dispose(); _banzai = null; }
    }

    private void DisposeMapAndTeams()
    {
        if (_gamemap != null) { _gamemap.Dispose(); _gamemap = null; }
        if (_gameItemHandler != null) { _gameItemHandler.Dispose(); _gameItemHandler = null; }
        if (Teambanzai != null) { Teambanzai.Dispose(); Teambanzai = null; }
        if (Teamfreeze != null) { Teamfreeze.Dispose(); Teamfreeze = null; }
    }

    private void DisposeRoomManagers()
    {
        if (_roomUserManager != null) { _roomUserManager.Dispose(); _roomUserManager = null; }
        if (_roomItemHandling != null) { _roomItemHandling.Dispose(); _roomItemHandling = null; }
    }

    private void ClearRoomComponents()
    {
        if (WordFilterList.Count > 0) WordFilterList.Clear();
        _filterComponent?.Cleanup(); _wiredComponent?.Cleanup();
        _bansComponent?.Cleanup(); _tradingComponent?.Cleanup();
    }
}
