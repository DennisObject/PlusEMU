using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Achievements;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.Core;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Data.Moodlight;
using Plus.HabboHotel.Items.Data.Toner;
using Plus.HabboHotel.Rooms.Games;
using Plus.HabboHotel.Rooms.Games.Banzai;
using Plus.HabboHotel.Rooms.Games.Football;
using Plus.HabboHotel.Rooms.Games.Freeze;
using Plus.HabboHotel.Rooms.Games.Teams;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms;

public class Room
{
    private readonly IReadOnlyList<IRoomComponent> _components;
    private int _initiated;
    private RoomData? _data;
    private BansComponent _bansComponent;
    private FilterComponent _filterComponent;
    private Dictionary<uint, List<RoomUser>> _tents;
    private TradingComponent _tradingComponent;
    private WiredComponent _wiredComponent;
    private BattleBanzai _banzai;
    private Freeze _freeze;
    private GameItemHandler _gameItemHandler;
    private GameManager _gameManager;

    private Gamemap _gamemap;
    private RoomItemHandling _roomItemHandling;

    private RoomUserManager _roomUserManager;
    private TimeProvider? _interactionClock;
    private IRoomUserSnapshotService _userSnapshots;
    private Soccer _soccer;

    public bool IsCrashed;
    public DateTimeOffset? LastRegenerationAt;
    public DateTimeOffset? LastTimerResetAt;
    public bool MDisposed;
    public MoodlightData MoodlightData;

    public Dictionary<int, DateTimeOffset> MutedUsers;

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

    private readonly ILogger<RoomNavigation> _navigationLogger;
    private readonly ILogger _wiredLogger;
    private readonly IAchievementManager _achievements;
    private readonly IRoomManager _rooms;

    public Room(RoomData data, IEnumerable<IRoomComponent> components, ILogger<RoomNavigation> navigationLogger, ILogger wiredLogger, IAchievementManager achievements, IRoomManager rooms)
    {
        _data = data;
        _components = components.OrderBy(component => component.Order).ToArray();
        _navigationLogger = navigationLogger;
        _wiredLogger = wiredLogger;
        _achievements = achievements;
        _rooms = rooms;
    }


    public RoomData Data => _data ??= new RoomData();
    public static implicit operator RoomData(Room room) => room.Data;
    internal IReadOnlyList<IRoomComponent> Components => _components;

    public void Initiate()
    {
        if (Interlocked.Exchange(ref _initiated, 1) != 0) {
            throw new InvalidOperationException($"Room {Data.Id} has already been initiated.");
        }

        foreach (var component in _components) {
            component.Initiate(this);
        }

        foreach (var component in _components) {
            component.Initiated();
        }
    }

    internal void SetRuntime(Gamemap gamemap, RoomItemHandling items, RoomUserManager users,
        WiredComponent wired, IRoomUserSnapshotService userSnapshots, TimeProvider interactionClock)
    {
        IsLagging = 0;
        Unloaded = false;
        IdleTime = 0;
        RoomMuted = false;
        MutedUsers = new();
        _tents = new();
        _gamemap = gamemap;
        _roomItemHandling = items;
        _roomUserManager = users;
        _wiredComponent = wired;
        _userSnapshots = userSnapshots;
        _interactionClock = interactionClock;
        LastRegenerationAt = interactionClock.GetUtcNow();
    }

    internal void SetBans(BansComponent bans) => _bansComponent = bans;
    internal void SetFilter(FilterComponent filter) => _filterComponent = filter;
    internal void SetTrading(TradingComponent trading) => _tradingComponent = trading;

    internal ILogger<RoomNavigation> NavigationLogger => _navigationLogger;
    internal ILogger WiredLogger => _wiredLogger;
    internal TimeProvider InteractionClock => _interactionClock
        ?? throw new InvalidOperationException("The room interaction clock has not been initialized.");
    internal TimeProvider RuntimeClock => InteractionClock;

    public uint Id { get => Data.Id; set => Data.Id = value; }
    public string Name { get => Data.Name; set => Data.Name = value; }
    public string ModelName { get => Data.ModelName; set => Data.ModelName = value; }
    public string OwnerName { get => Data.OwnerName; set => Data.OwnerName = value; }
    public int OwnerId { get => Data.OwnerId; set => Data.OwnerId = value; }
    public string Password { get => Data.Password; set => Data.Password = value; }
    public int Score { get => Data.Score; set => Data.Score = value; }
    public RoomAccess Access { get => Data.Access; set => Data.Access = value; }
    public string Type { get => Data.Type; set => Data.Type = value; }
    public int UsersMax { get => Data.UsersMax; set => Data.UsersMax = value; }
    public int UsersNow { get => Data.UsersNow; set => Data.UsersNow = value; }
    public int Category { get => Data.Category; set => Data.Category = value; }
    public string Description { get => Data.Description; set => Data.Description = value; }
    public List<string> Tags => Data.Tags;
    public string Floor { get => Data.Floor; set => Data.Floor = value; }
    public string Landscape { get => Data.Landscape; set => Data.Landscape = value; }
    public bool AllowPets { get => Data.AllowPets; set => Data.AllowPets = value; }
    public bool AllowPetsEating { get => Data.AllowPetsEating; set => Data.AllowPetsEating = value; }
    public bool RoomBlockingEnabled { get => Data.RoomBlockingEnabled; set => Data.RoomBlockingEnabled = value; }
    public bool Hidewall { get => Data.Hidewall; set => Data.Hidewall = value; }
    public int WallThickness { get => Data.WallThickness; set => Data.WallThickness = value; }
    public int FloorThickness { get => Data.FloorThickness; set => Data.FloorThickness = value; }
    public string Wallpaper { get => Data.Wallpaper; set => Data.Wallpaper = value; }
    public int WhoCanMute { get => Data.WhoCanMute; set => Data.WhoCanMute = value; }
    public int WhoCanBan { get => Data.WhoCanBan; set => Data.WhoCanBan = value; }
    public int WhoCanKick { get => Data.WhoCanKick; set => Data.WhoCanKick = value; }
    public int ChatMode { get => Data.ChatMode; set => Data.ChatMode = value; }
    public int ChatSize { get => Data.ChatSize; set => Data.ChatSize = value; }
    public int ChatSpeed { get => Data.ChatSpeed; set => Data.ChatSpeed = value; }
    public int ExtraFlood { get => Data.ExtraFlood; set => Data.ExtraFlood = value; }
    public int ChatDistance { get => Data.ChatDistance; set => Data.ChatDistance = value; }
    public int TradeSettings { get => Data.TradeSettings; set => Data.TradeSettings = value; }
    public bool PushEnabled { get => Data.PushEnabled; set => Data.PushEnabled = value; }
    public bool PullEnabled { get => Data.PullEnabled; set => Data.PullEnabled = value; }
    public bool SuperPushEnabled { get => Data.SuperPushEnabled; set => Data.SuperPushEnabled = value; }
    public bool SuperPullEnabled { get => Data.SuperPullEnabled; set => Data.SuperPullEnabled = value; }
    public bool EnablesEnabled { get => Data.EnablesEnabled; set => Data.EnablesEnabled = value; }
    public bool RespectNotificationsEnabled { get => Data.RespectNotificationsEnabled; set => Data.RespectNotificationsEnabled = value; }
    public bool PetMorphsAllowed { get => Data.PetMorphsAllowed; set => Data.PetMorphsAllowed = value; }
    public int SalePrice { get => Data.SalePrice; set => Data.SalePrice = value; }
    public bool ReverseRollers { get => Data.ReverseRollers; set => Data.ReverseRollers = value; }
    public bool LayEnabled { get => Data.LayEnabled; set => Data.LayEnabled = value; }
    public RoomModel Model { get => Data.Model; set => Data.Model = value; }
    public RoomPromotion Promotion { get => Data.Promotion; set => Data.Promotion = value; }
    public Plus.HabboHotel.Groups.Group Group { get => Data.Group; set => Data.Group = value; }
    public bool HasActivePromotion => Data.HasActivePromotion;
    public void EndPromotion() => Data.EndPromotion();

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
        return _roomItemHandling;
    }

    public RoomUserManager GetRoomUserManager() => _roomUserManager;

    public Soccer GetSoccer()
    {
        if (_soccer == null) {
            _soccer = new(this);
        }

        return _soccer;
    }

    public TeamManager GetTeamManagerForBanzai()
    {
        if (Teambanzai == null) {
            Teambanzai = TeamManager.CreateTeam("banzai");
        }

        return Teambanzai;
    }

    public TeamManager GetTeamManagerForFreeze()
    {
        if (Teamfreeze == null) {
            Teamfreeze = TeamManager.CreateTeam("freeze");
        }

        return Teamfreeze;
    }

    public BattleBanzai GetBanzai()
    {
        if (_banzai == null) {
            _banzai = new(this, RuntimeClock, _achievements);
        }

        return _banzai;
    }

    public Freeze GetFreeze()
    {
        if (_freeze == null) {
            _freeze = new(this);
        }

        return _freeze;
    }

    public GameManager GetGameManager()
    {
        if (_gameManager == null) {
            _gameManager = new(this, RuntimeClock);
        }

        return _gameManager;
    }

    public GameItemHandler GetGameItemHandler()
    {
        if (_gameItemHandler == null) {
            _gameItemHandler = new(this);
        }

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

    public FilterComponent GetFilter() => _filterComponent;

    public WiredComponent GetWired() => _wiredComponent;

    public BansComponent GetBans() => _bansComponent;

    public TradingComponent GetTrading() => _tradingComponent;

    public bool CheckRights(GameClient session) => CheckRights(session, false);

    public bool CheckRights(GameClient session, bool requireOwnership, bool checkForGroups = false)
    {
        try {
            if (session == null || session.GetHabbo() == null) {
                return false;
            }

            if (session.GetHabbo().Username == OwnerName && Type == "private") {
                return true;
            }

            if (session.GetHabbo().Access.Can(PermissionKeys.RoomOwnerAny)) {
                return true;
            }

            if (!requireOwnership && Type == "private") {
                if (session.GetHabbo().Access.Can(PermissionKeys.RoomRightsAny)) {
                    return true;
                }

                if (UsersWithRights.Contains(session.GetHabbo().Id)) {
                    return true;
                }
            }

            if (checkForGroups && Type == "private") {
                if (Group == null) {
                    return false;
                }

                if (Group.IsAdmin(session.GetHabbo().Id)) {
                    return true;
                }

                if (Group.AdminOnlyDeco == 0) {
                    if (Group.IsAdmin(session.GetHabbo().Id)) {
                        return true;
                    }
                }
            }
        }
        catch (Exception e) {
            ExceptionLogger.LogException(e);
        }

        return false;
    }

    public void OnUserShoot(RoomUser user, Item ball)
    {
        Func<Item, bool>? predicate = null;
        string? key = null;

        foreach (var item in GetRoomItemHandler().GetFurniObjects(ball.GetX, ball.GetY).ToList()) {
            if (item.Definition.ItemName.StartsWith("fball_goal_")) {
                key = item.Definition.ItemName.Split(new[] { '_' })[2];
                user.UnIdle();
                user.DanceId = 0;
                _achievements.ProgressAchievement(user.GetClient(), "ACH_FootballGoalScored", 1);
                SendPacket(new ActionComposer(user.VirtualId, 1));
            }
        }

        if (key != null) {
            if (predicate == null) {
                predicate = p => p.Definition.ItemName == $"fball_score_{key}";
            }

            foreach (var item2 in GetRoomItemHandler().GetFloor.Where(predicate).ToList()) {
                if (item2.Definition.ItemName == $"fball_score_{key}") {
                    if (!string.IsNullOrEmpty(item2.LegacyDataString)) {
                        item2.LegacyDataString = (Convert.ToInt32(item2.LegacyDataString) + 1).ToString();
                    }
                    else {
                        item2.LegacyDataString = "1";
                    }

                    item2.UpdateState();
                }
            }
        }
    }

    internal void ProcessWiredOnly()
    {
        if (UsesV2Movement) {
            lock (NavigationSync) {
                ProcessWiredOwned();
            }

            return;
        }

        if (IsCrashed || MDisposed) {
            return;
        }

        try {
            GetWired().OnFastCycle();
        }
        catch (Exception e) {
            ExceptionLogger.LogException(e);
        }
    }

    // v2 fast pass: serialized with ProcessRoom (one process task at a time), so it owns the room just like a
    // full tick. Legacy and shadow rooms keep the original pass, with no owner scope.
    internal void RunFastPass(Action pass)
    {
        if (UsesV2Movement) {
            lock (NavigationSync) {
                RunOwnedPass(pass);
            }

            return;
        }

        if (IsCrashed || MDisposed) {
            return;
        }

        try {
            pass();
        }
        catch (Exception e) {
            ExceptionLogger.LogException(e);
        }
    }

    public void ProcessRoom()
    {
        if (UsesV2Movement) {
            lock (NavigationSync) {
                ProcessRoomOwned();
            }

            return;
        }

        if (IsCrashed || MDisposed) {
            return;
        }

        try {
            if (GetRoomUserManager().GetRoomUsers().Count == 0) {
                IdleTime++;
            }
            else if (IdleTime > 0) {
                IdleTime = 0;
            }

            if (HasActivePromotion && Promotion.HasExpired) {
                EndPromotion();
            }

            if (IdleTime >= 60 && !HasActivePromotion) {
                _rooms.UnloadRoom(Id);

                return;
            }

            CycleComponents();

            try {
                GetGameMap().Navigation?.ApplyDirty();
                GetRoomItemHandler().OnCycle();
            }
            catch (Exception e) {
                ExceptionLogger.LogException(e);
            }

            try {
                GetGameMap().Navigation?.ApplyDirty();
                GetRoomUserManager().OnCycle();
            }
            catch (Exception e) {
                ExceptionLogger.LogException(e);
            }

            try {
                GetRoomUserManager().SerializeStatusUpdates();
            }
            catch (Exception e) {
                ExceptionLogger.LogException(e);
            }

            try {
                if (_gameItemHandler != null) {
                    _gameItemHandler.OnCycle();
                }
            }
            catch (Exception e) {
                ExceptionLogger.LogException(e);
            }

            try {
                GetWired().OnCycle();
                GetGameMap().FlushPlacementUpdates();
            }
            catch (Exception e) {
                ExceptionLogger.LogException(e);
            }
        }
        catch (Exception e) {
            ExceptionLogger.LogException(e);
            OnRoomCrash(e);
        }
    }

    private void ProcessRoomOwned()
    {
        if (IsCrashed || MDisposed) {
            return;
        }

        using var owner = Plus.HabboHotel.Rooms.PathFinding.RoomOwnerScope.Enter(this);

        try {
            if (!KeepRoomActive()) {
                return;
            }

            RunRoomPhase(CycleComponents);
            RunRoomPhase(CycleFurniture);
            RunRoomPhase(CycleActors);
            RunRoomPhase(() => GetRoomUserManager().SerializeStatusUpdates());
            RunRoomPhase(() => _gameItemHandler?.OnCycle());
            RunRoomPhase(CycleWired);
        }
        catch (Exception error) {
            ExceptionLogger.LogException(error);
            OnRoomCrash(error);
        }
    }

    private void CycleComponents()
    {
        foreach (var component in _components) {
            component.Cycle();
        }
    }

    private bool KeepRoomActive()
    {
        if (GetRoomUserManager().GetRoomUsers().Count == 0) {
            IdleTime++;
        }
        else if (IdleTime > 0) {
            IdleTime = 0;
        }

        if (HasActivePromotion && Promotion.HasExpired) {
            EndPromotion();
        }

        if (IdleTime < 60 || HasActivePromotion) {
            return true;
        }

        _rooms.UnloadRoom(Id);

        return false;
    }

    private void RunRoomPhase(Action phase)
    {
        if (MDisposed) {
            return;
        }

        try {
            phase();
        }
        catch (Exception error) {
            ExceptionLogger.LogException(error);
        }
    }

    private void CycleFurniture()
    {
        GetGameMap().Navigation?.ApplyDirty();
        GetGameMap().Navigation?.DrainCommands();

        if (UsesV2Movement) {
            GetGameMap().Gates.Drain();
        }

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
        try {
            foreach (var user in _roomUserManager.GetRoomUsers().ToList()) {
                if (user == null || user.GetClient() == null) {
                    continue;
                }

                user.GetClient().SendNotification("Sorry, it appears that room has crashed!"); //Unhandled exception in room: " + e);

                try {
                    GetRoomUserManager().RemoveUserFromRoom(user.GetClient(), true);
                }
                catch (Exception e2) {
                    ExceptionLogger.LogException(e2);
                }
            }
        }
        catch (Exception e3) {
            ExceptionLogger.LogException(e3);
        }

        IsCrashed = true;
        _rooms.UnloadRoom(Id);
    }


    public bool CheckMute(GameClient session, DateTimeOffset now)
    {
        if (MutedUsers.TryGetValue(session.GetHabbo().Id, out var mutedUntil)) {
            if (now >= mutedUntil) {
                MutedUsers.Remove(session.GetHabbo().Id);
            }
            else {
                return true;
            }
        }

        if (session.GetHabbo().TimeMuted > 0 || RoomMuted && session.GetHabbo().Username != OwnerName) {
            return true;
        }

        return false;
    }

    public void SendObjects(GameClient session)
    {
        GetGameMap().SendPlacementHeightMap(session);
        session.Send(new FloorHeightMapComposer(GetGameMap().Model.GetRelativeHeightmap(), GetGameMap().StaticModel.WallHeight));
        var snapshotUsers = _roomUserManager.GetUserList().Where(user => user != null).ToArray();

        foreach (var user in snapshotUsers) {
            if (user == null) {
                continue;
            }

            var userSnapshot = _userSnapshots.Capture(user);

            if (userSnapshot != null) {
                session.Send(new UsersComposer(userSnapshot));
            }

            if (user.IsBot && user.BotData.DanceId > 0) {
                session.Send(new DanceComposer(user.VirtualId, user.BotData.DanceId));
            }
            else if (!user.IsBot && !user.IsPet && user.IsDancing) {
                session.Send(new DanceComposer(user.VirtualId, user.DanceId));
            }

            if (user.IsAsleep) {
                session.Send(new SleepComposer(user.VirtualId, true));
            }

            if (user.CarryItemId > 0 && user.CarryTimer > 0) {
                session.Send(new CarryObjectComposer(user.VirtualId, user.CarryItemId));
            }

            if (!user.IsBot && !user.IsPet && user.CurrentEffect > 0) {
                session.Send(new AvatarEffectComposer(user.VirtualId, user.CurrentEffect));
            }
        }

        session.Send(new UserUpdateComposer(RoomUserStatusSnapshot.Capture(_roomUserManager.GetUserList())));
        var snapshotFurniture = GetRoomItemHandler().GetFloor.ToArray();
        session.Send(new ObjectsComposer(RoomFurnitureSnapshot.Capture(snapshotFurniture, OwnerId, OwnerName)));
        var snapshotWalls = GetRoomItemHandler().GetWall.ToArray();
        session.Send(new ItemsComposer(RoomFurnitureSnapshot.Capture(snapshotWalls, OwnerId, OwnerName)));
        _wiredComponent?.SnapshotEnqueued(session, snapshotFurniture.Concat(snapshotWalls), snapshotUsers);
    }

    public void AddTent(uint tentId)
    {
        if (_tents.ContainsKey(tentId)) {
            _tents.Remove(tentId);
        }

        _tents.Add(tentId, new());
    }

    public void RemoveTent(uint tentId)
    {
        if (!_tents.ContainsKey(tentId)) {
            return;
        }

        var users = _tents[tentId];

        foreach (var user in users.ToList()) {
            if (user == null || user.GetClient() == null || user.GetClient().GetHabbo() == null) {
                continue;
            }

            user.GetClient().GetHabbo().TentId = 0;
        }

        if (_tents.ContainsKey(tentId)) {
            _tents.Remove(tentId);
        }
    }

    public void AddUserToTent(uint tentId, RoomUser user)
    {
        if (user != null && user.GetClient() != null && user.GetClient().GetHabbo() != null) {
            if (!_tents.ContainsKey(tentId)) {
                _tents.Add(tentId, new());
            }

            if (!_tents[tentId].Contains(user)) {
                _tents[tentId].Add(user);
            }

            user.GetClient().GetHabbo().TentId = tentId;
        }
    }

    public void RemoveUserFromTent(uint tentId, RoomUser user)
    {
        if (user != null && user.GetClient() != null && user.GetClient().GetHabbo() != null) {
            if (!_tents.ContainsKey(tentId)) {
                _tents.Add(tentId, new());
            }

            if (_tents[tentId].Contains(user)) {
                _tents[tentId].Remove(user);
            }

            user.GetClient().GetHabbo().TentId = 0;
        }
    }

    public void SendToTent(int id, uint tentId, IServerPacket packet)
    {
        if (!_tents.ContainsKey(tentId)) {
            return;
        }

        foreach (var user in _tents[tentId].ToList()) {
            if (user == null || user.GetClient() == null || user.GetClient().GetHabbo() == null || user.GetClient().GetHabbo().IgnoresComponent.IsIgnored(id) ||
                user.GetClient().GetHabbo().TentId != tentId) {
                continue;
            }

            user.GetClient().Send(packet);
        }
    }

    public void SendPacket(IServerPacket packet, bool withRightsOnly = false) => SendPacket(packet, withRightsOnly, null);

    public void SendObject(Item item) => SendPacket(item.IsWallItem ? new ItemAddComposer(RoomItemSnapshot.Capture(item)) : new ObjectAddComposer(RoomItemSnapshot.Capture(item)), false,
        viewer => _wiredComponent?.ObjectEnqueued(viewer, item, null));

    public void SendUser(RoomUser user)
    {
        var snapshot = _userSnapshots.Capture(user);

        if (snapshot != null) {
            SendPacket(new UsersComposer(snapshot), false, viewer => _wiredComponent?.ObjectEnqueued(viewer, null, user));
        }
    }

    private void SendPacket(IServerPacket packet, bool withRightsOnly, Action<RoomUser>? enqueued)
    {
        if (packet == null) {
            return;
        }

        try {
            if (_roomUserManager == null) {
                return;
            }

            GameClient.SendBroadcast(packet, GetRecipients());

            IEnumerable<GameClient> GetRecipients()
            {
                foreach (var user in _roomUserManager.GetRoomUsers()) {
                    var client = user?.GetClient();

                    if (client == null || withRightsOnly && !CheckRights(client)) {
                        continue;
                    }

                    yield return client;
                    // SendBroadcast sends inside its loop, so this runs after the packet is queued for this viewer.
                    enqueued?.Invoke(user);
                }
            }
        }
        catch (Exception e) {
            ExceptionLogger.LogException(e);
        }
    }

    public void SendPacket(List<IServerPacket> packets)
    {
        foreach (var packet in packets) {
            SendPacket(packet);
        }
    }

    public void Dispose()
    {
        if (UsesV2Movement) {
            DisposeExecutorRoom();

            return;
        }

        if (MDisposed) {
            return;
        }

        IsCrashed = false;
        MDisposed = true;
        _gamemap?.ClosePlacementUpdates();

        // Drop every user before the managers are destroyed. A habbo left
        // pointing at this room makes the next enter throw and disconnect.
        if (_roomUserManager != null) {
            foreach (var user in _roomUserManager.GetRoomUsers().ToList()) {
                var client = user?.GetClient();

                if (client == null) {
                    continue;
                }

                _roomUserManager.RemoveUserFromRoom(client, true);
            }
        }

        /* TODO: Needs reviewing */
        try {
            if (ProcessTask != null && ProcessTask.IsCompleted) {
                ProcessTask.Dispose();
            }
        }
        catch { }

        TonerData = null;
        MoodlightData = null;

        if (MutedUsers.Count > 0) {
            MutedUsers.Clear();
        }

        if (_tents.Count > 0) {
            _tents.Clear();
        }

        if (UsersWithRights.Count > 0) {
            UsersWithRights.Clear();
        }

        if (_gameManager != null) {
            _gameManager.Dispose();
            _gameManager = null;
        }

        if (_freeze != null) {
            _freeze.Dispose();
            _freeze = null;
        }

        if (_soccer != null) {
            _soccer.Dispose();
            _soccer = null;
        }

        if (_banzai != null) {
            _banzai.Dispose();
            _banzai = null;
        }

        if (_gamemap != null) {
            _gamemap.Dispose();
            _gamemap = null;
        }

        if (_gameItemHandler != null) {
            _gameItemHandler.Dispose();
            _gameItemHandler = null;
        }

        // Room Data?
        if (Teambanzai != null) {
            Teambanzai.Dispose();
            Teambanzai = null;
        }

        if (Teamfreeze != null) {
            Teamfreeze.Dispose();
            Teamfreeze = null;
        }

        if (_roomUserManager != null) {
            _roomUserManager.Dispose();
            _roomUserManager = null;
        }

        if (_roomItemHandling != null) {
            _roomItemHandling.Dispose();
            _roomItemHandling = null;
        }

        if (WordFilterList.Count > 0) {
            WordFilterList.Clear();
        }

        if (_filterComponent != null) {
            _filterComponent.Cleanup();
        }

        if (_wiredComponent != null) {
            _wiredComponent.Cleanup();
        }

        if (_bansComponent != null) {
            _bansComponent.Cleanup();
        }

        if (_tradingComponent != null) {
            _tradingComponent.Cleanup();
        }
    }

    private void ProcessWiredOwned() => RunOwnedPass(() => GetWired().OnFastCycle());

    // The fast pass owns the room for the gate sequencer, so Wired closes run inline and in order.
    private void RunOwnedPass(Action pass)
    {
        if (IsCrashed || MDisposed) {
            return;
        }

        using var owner = Plus.HabboHotel.Rooms.PathFinding.RoomOwnerScope.Enter(this);

        try {
            pass();
        }
        catch (Exception e) {
            ExceptionLogger.LogException(e);
        }
    }

    private void DisposeExecutorRoom()
    {
        lock (NavigationSync) {
            if (MDisposed) {
                return;
            }

            IsCrashed = false;
            MDisposed = true;
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
        if (_roomUserManager == null) {
            return;
        }

        foreach (var user in _roomUserManager.GetRoomUsers().ToList()) {
            var client = user?.GetClient();

            if (client != null) {
                _roomUserManager.RemoveUserFromRoom(client, true);
            }
        }
    }

    private void DisposeCompletedTask()
    {
        try {
            if (ProcessTask is { IsCompleted: true }) {
                ProcessTask.Dispose();
            }
        }
        catch { }
    }

    private void ClearRoomCollections()
    {
        TonerData = null;
        MoodlightData = null;

        if (MutedUsers.Count > 0) {
            MutedUsers.Clear();
        }

        if (_tents.Count > 0) {
            _tents.Clear();
        }

        if (UsersWithRights.Count > 0) {
            UsersWithRights.Clear();
        }
    }

    private void DisposeGames()
    {
        if (_gameManager != null) {
            _gameManager.Dispose();
            _gameManager = null;
        }

        if (_freeze != null) {
            _freeze.Dispose();
            _freeze = null;
        }

        if (_soccer != null) {
            _soccer.Dispose();
            _soccer = null;
        }

        if (_banzai != null) {
            _banzai.Dispose();
            _banzai = null;
        }
    }

    private void DisposeMapAndTeams()
    {
        if (_gamemap != null) {
            _gamemap.Dispose();
            _gamemap = null;
        }

        if (_gameItemHandler != null) {
            _gameItemHandler.Dispose();
            _gameItemHandler = null;
        }

        if (Teambanzai != null) {
            Teambanzai.Dispose();
            Teambanzai = null;
        }

        if (Teamfreeze != null) {
            Teamfreeze.Dispose();
            Teamfreeze = null;
        }
    }

    private void DisposeRoomManagers()
    {
        if (_roomUserManager != null) {
            _roomUserManager.Dispose();
            _roomUserManager = null;
        }

        if (_roomItemHandling != null) {
            _roomItemHandling.Dispose();
            _roomItemHandling = null;
        }
    }

    private void ClearRoomComponents()
    {
        if (WordFilterList.Count > 0) {
            WordFilterList.Clear();
        }

        _filterComponent?.Cleanup();
        _wiredComponent?.Cleanup();
        _bansComponent?.Cleanup();
        _tradingComponent?.Cleanup();
    }
}
