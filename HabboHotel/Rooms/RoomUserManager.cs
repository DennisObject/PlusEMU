using Plus.Communication.Packets;
using System.Diagnostics.CodeAnalysis;
using Plus.HabboHotel.Permissions;
using System.Collections.Concurrent;
using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Permissions;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.Core;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Speech;
using Plus.HabboHotel.Rooms.Chat.Emotions;
using Plus.Core.FigureData;
using Plus.HabboHotel.Rooms.Games.Teams;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Rooms.Trading;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms;

public class RoomUserManager
{
    public const int MaxStressBots = 1000;
    private readonly object _stressSync = new();
    private readonly Queue<(int Amount, int OwnerId, Action<string> Reply)> _stressRequests = new();
    private bool _disposed;
    private int _nextStressBotId;

    private ConcurrentDictionary<int, RoomUser> _bots;
    private ConcurrentDictionary<int, RoomUser> _pets;

    private int _primaryPrivateUserId;
    private Room _room;
    private readonly IRoomUserStore _store;
    private readonly TimeProvider _clock;
    private readonly IRewardTrackManager _rewards;
    private readonly IChatEmotionsManager _chatEmotions;
    private readonly IBotAiFactory _botAiFactory;
    private readonly IGameClientManager _clients;
    private readonly IItemTravelStore _travelStore;
    private ConcurrentDictionary<int, RoomUser> _users;

    public int UserCount;


    public RoomUserManager(Room room, IRoomUserStore store, TimeProvider clock, IRewardTrackManager rewards,
        IChatEmotionsManager chatEmotions, IBotAiFactory botAiFactory, IGameClientManager clients, IItemTravelStore travelStore)
    {
        _room = room;
        _store = store;
        _clock = clock;
        _rewards = rewards;
        _chatEmotions = chatEmotions;
        _botAiFactory = botAiFactory;
        _clients = clients;
        _travelStore = travelStore;
        _users = new();
        _pets = new();
        _bots = new();
        _primaryPrivateUserId = 0;
        PetCount = 0;
        UserCount = 0;
    }

    public int PetCount { get; private set; }

    // Requests are bounded and applied on the cycle thread, before its user snapshot.
    public bool QueueStressBots(int amount, int ownerId, Action<string> reply)
    {
        if (amount < 0 || amount > MaxStressBots)
            return false;
        lock (_stressSync)
        {
            if (_disposed || _stressRequests.Count >= 8)
                return false;
            _stressRequests.Enqueue((amount, ownerId, reply));
            return true;
        }
    }

    internal void ProcessStressBots()
    {
        lock (_stressSync)
        {
            if (_disposed || !_stressRequests.TryDequeue(out var request))
                return;
            var temporary = _bots.Values.Where(user => user.BotData.IsTemporary).ToList();
            if (request.Amount == 0)
            {
                foreach (var user in temporary)
                {
                    _room.GetGameMap().RemoveUserFromMap(user, user.Coordinate);
                    RemoveBot(user.VirtualId, false);
                }
                request.Reply($"Cleared {temporary.Count} temporary stress bots.");
                return;
            }
            if (temporary.Count + request.Amount > MaxStressBots)
            {
                request.Reply($"This room has {temporary.Count} stress bots; the limit is {MaxStressBots}. Use :stress bots clear first.");
                return;
            }
            var model = _room.GetGameMap().Model;
            for (var i = 0; i < request.Amount; i++)
            {
                // Negative IDs cannot collide with database/inventory bots.
                var id = --_nextStressBotId;
                var speeches = new List<RandomSpeech> { new($"Stress bot {-id} checking room traffic.", id) };
                var bot = new RoomBot(id, _room.RoomId, "generic", "freeroam", $"Stress {-id}", "Temporary stress bot",
                    IFigureDataManager.DefaultFigure, model.DoorX, model.DoorY, model.DoorZ, model.DoorOrientation,
                    0, 0, 0, 0, ref speeches, "M", 0, request.OwnerId, true, 60, false, 0) { IsTemporary = true };
                var user = DeployBot(bot, null);
                user.AllowOverride = true;
                user.BotAi.OnSelfEnterRoom();
            }
            request.Reply($"Created {request.Amount} temporary stress bots ({temporary.Count + request.Amount}/{MaxStressBots}). Use :stress bots clear to remove them.");
        }
    }

    private bool UsesV2Movement => _room.UsesV2Movement;

    private void PublishSpawn(RoomUser actor)
    {
        if (!UsesV2Movement) return;
        _users.TryAdd(actor.InternalRoomId, actor);
        AdmitSpawn(actor);
    }

    private void AdmitSpawn(RoomUser actor)
    {
        if (UsesV2Movement) _room.GetGameMap().Navigation!.Admit(actor);
    }

    public RoomUser DeployBot(RoomBot bot, Pet pet)
    {
        var user = new RoomUser(0, _room.RoomId, Interlocked.Increment(ref _primaryPrivateUserId) - 1, _room, null, _chatEmotions, _rewards);
        bot.VirtualId = user.VirtualId;
        var personalId = user.VirtualId;
        user.InternalRoomId = personalId;
        if (!UsesV2Movement) _users.TryAdd(personalId, user);
        var model = _room.GetGameMap().Model;
        if (bot.X > 0 && bot.Y > 0 && bot.X < model.MapSizeX && bot.Y < model.MapSizeY)
        {
            if (UsesV2Movement) user.InitializePosition(bot.X, bot.Y, bot.Z);
            else user.SetPos(bot.X, bot.Y, bot.Z);
            user.SetRot(bot.Rot, false);
        }
        else
        {
            bot.X = model.DoorX;
            bot.Y = model.DoorY;
            if (UsesV2Movement) user.InitializePosition(model.DoorX, model.DoorY, model.DoorZ);
            else user.SetPos(model.DoorX, model.DoorY, model.DoorZ);
            user.SetRot(model.DoorOrientation, false);
        }
        user.BotData = bot;
        user.BotAi = _botAiFactory.Create(bot.AiType, user.VirtualId);
        if (user.IsPet)
        {
            user.BotAi.Init(bot.BotId, user.VirtualId, _room.RoomId, user, _room);
            user.PetData = pet;
            user.PetData.VirtualId = user.VirtualId;
            user.PetData.Attach(_room, _clients, _rewards);
        }
        else
            user.BotAi.Init(bot.BotId, user.VirtualId, _room.RoomId, user, _room);
        user.UpdateNeeded = true;
        if (UsesV2Movement) PublishSpawn(user);
        _room.SendUser(user);
        if (user.IsPet)
        {
            if (_pets.ContainsKey(user.PetData.PetId))
                _pets[user.PetData.PetId] = user;
            else
                _pets.TryAdd(user.PetData.PetId, user);
            PetCount++;
        }
        else if (user.IsBot)
        {
            if (_bots.ContainsKey(user.BotData.BotId))
                _bots[user.BotData.BotId] = user;
            else
                _bots.TryAdd(user.BotData.Id, user);
            _room.SendPacket(new DanceComposer(user.VirtualId, user.BotData.DanceId));
        }
        return user;
    }

    public void RemoveBot(int virtualId, bool kicked)
    {
        var user = GetRoomUserByVirtualId(virtualId);
        if (user == null || !user.IsBot)
            return;
        if (_users == null || !_users.TryRemove(new KeyValuePair<int, RoomUser>(user.InternalRoomId, user)))
            return;
        try
        {
            if (_room.GetGameMap().Navigation is { UsesExecutor: true } navigation) navigation.Remove(user);
            if (user.IsPet)
            {
                if (_pets.TryRemove(new KeyValuePair<int, RoomUser>(user.PetData.PetId, user)))
                    PetCount--;
            }
            else
                _bots.TryRemove(new KeyValuePair<int, RoomUser>(user.BotData.Id, user));
            _room.GetWired()?.BeforeActorLeaves(user);
            user.BotAi.OnSelfLeaveRoom(kicked);
            _room.SendPacket(new UserRemoveComposer(user.VirtualId));
            _room.GetWired()?.Dispatch(new WiredRuntimeEvent(WiredEventKind.Leave) { Actor = user });
            OnRemove(user);
        }
        finally
        {
            user.BotAi?.Detach(_room, user);
            user.PetData?.Detach(_room);
            user.Dispose();
        }
    }

    public RoomUser? GetUserForSquare(int x, int y) => _room.GetGameMap().GetRoomUsers(new(x, y)).FirstOrDefault();

    public bool AddAvatarToRoom(GameClient session)
    {
        if (_room == null)
            return false;
        if (session == null)
            return false;
        if (session.GetHabbo().CurrentRoom == null)
            return false;
        if (_users.Any(u => u.Value.UserId == session.GetHabbo().Id))
            return false;
        var user = new RoomUser(session.GetHabbo().Id, _room.RoomId, Interlocked.Increment(ref _primaryPrivateUserId) - 1, _room, session, _chatEmotions, _rewards);
        user.UserId = session.GetHabbo().Id;
        session.GetHabbo().TentId = 0;
        var personalId = user.VirtualId;
        user.InternalRoomId = personalId;
        session.GetHabbo().CurrentRoom = _room;
        if (!UsesV2Movement && !_users.TryAdd(personalId, user))
            return false;
        user.WiredRoomEntry = WiredRoomEntrySnapshot.Capture(_room, session.GetHabbo());
        var model = _room.GetGameMap().Model;
        if (model == null)
            return false;
        if (!_room.PetMorphsAllowed && session.GetHabbo().PetId != 0)
            session.GetHabbo().PetId = 0;
        if (!session.GetHabbo().IsTeleporting && !session.GetHabbo().IsHopping)
        {
            if (!model.DoorIsValid())
            {
                var square = _room.GetGameMap().GetRandomWalkableSquare();
                model.DoorX = square.X;
                model.DoorY = square.Y;
                model.DoorZ = (int)_room.GetGameMap().GetHeightForSquareFromData(square);
            }
            if (UsesV2Movement) user.InitializePosition(model.DoorX, model.DoorY, model.DoorZ);
            else user.SetPos(model.DoorX, model.DoorY, model.DoorZ);
            user.SetRot(model.DoorOrientation, false);
        }
        else if (!user.IsBot && (session.GetHabbo().IsTeleporting || session.GetHabbo().IsHopping))
        {
            Item? item = null;
            if (session.GetHabbo().IsTeleporting)
                item = _room.GetRoomItemHandler().GetItem(session.GetHabbo().TeleporterId);
            else if (session.GetHabbo().IsHopping)
                item = _room.GetRoomItemHandler().GetItem(session.GetHabbo().HopperId);
            if (item != null)
            {
                if (session.GetHabbo().IsTeleporting)
                {
                    item.LegacyDataString = "2";
                    item.UpdateState(false, true);
                    if (UsesV2Movement) user.InitializePosition(item.GetX, item.GetY, item.GetZ);
                    else user.SetPos(item.GetX, item.GetY, item.GetZ);
                    user.SetRot(item.Rotation, false);
                    if (session.GetHabbo().TeleporterId != 0)
                        _rewards.Progress(session, RewardTrackActions.Teleport);
                    item.InteractingUser2 = session.GetHabbo().Id;
                    item.LegacyDataString = "0";
                    item.UpdateState(false, true);
                }
                else if (session.GetHabbo().IsHopping)
                {
                    item.LegacyDataString = "1";
                    item.UpdateState(false, true);
                    if (UsesV2Movement) user.InitializePosition(item.GetX, item.GetY, item.GetZ);
                    else user.SetPos(item.GetX, item.GetY, item.GetZ);
                    user.SetRot(item.Rotation, false);
                    user.AllowOverride = false;
                    item.InteractingUser2 = session.GetHabbo().Id;
                    item.LegacyDataString = "2";
                    item.UpdateState(false, true);
                }
            }
            else
            {
                if (UsesV2Movement) user.InitializePosition(model.DoorX, model.DoorY, model.DoorZ - 1);
                else user.SetPos(model.DoorX, model.DoorY, model.DoorZ - 1);
                user.SetRot(model.DoorOrientation, false);
            }
        }
        if (UsesV2Movement && !_users.TryAdd(personalId, user)) return false;
        if (UsesV2Movement) AdmitSpawn(user);
        _room.SendUser(user);
        if (_room.CheckRights(session, true))
        {
            user.SetStatus("flatctrl", "useradmin");
            session.Send(new YouAreOwnerComposer());
            session.Send(new YouAreControllerComposer(4));
        }
        else if (_room.CheckRights(session, false) && _room.Group == null)
        {
            user.SetStatus("flatctrl", "1");
            session.Send(new YouAreControllerComposer(1));
        }
        else if (_room.Group != null && _room.CheckRights(session, false, true))
        {
            user.SetStatus("flatctrl", "3");
            session.Send(new YouAreControllerComposer(3));
        }
        else
            session.Send(new YouAreNotControllerComposer());
        user.UpdateNeeded = true;
        if (session.GetHabbo().Access.Can(PermissionKeys.ModerationTool) && !session.GetHabbo().DisableForcedEffects)
            session.GetHabbo().Effects.ApplyEffect(102);
        if (session.GetHabbo().IsAmbassador && !session.GetHabbo().DisableForcedEffects && !session.GetHabbo().Access.Can(PermissionKeys.ModerationTool))
            session.GetHabbo().Effects.ApplyEffect(178);
        foreach (var bot in _bots.Values.ToList())
        {
            if (bot == null || bot.BotAi == null)
                continue;
            bot.BotAi.OnUserEnterRoom(user);
        }
        if (session.GetHabbo().Id != _room.OwnerId)
            _rewards.Progress(session, RewardTrackActions.EnterOtherUsersRoom);
        var pendingFollow = session.GetHabbo().PendingFollowRoomId;
        if (pendingFollow != 0)
        {
            session.GetHabbo().PendingFollowRoomId = 0;
            if (pendingFollow == _room.RoomId)
                _rewards.Progress(session, RewardTrackActions.FollowFriend);
        }
        return true;
    }

    public void RemoveUserFromRoom(GameClient session, bool nofityUser, bool notifyKick = false)
    {
        RoomUser? removedUser = null;
        try
        {
            if (_room == null)
                return;
            if (session == null || session.GetHabbo() == null)
                return;
            var user = GetUserList().FirstOrDefault(candidate => !candidate.IsBot && ReferenceEquals(candidate.GetClient(), session));
            if (user == null)
            {
                if (GetRoomUserByHabbo(session.GetHabbo().Id) != null || !ReferenceEquals(session.GetHabbo().CurrentRoom, _room))
                    return;
            }
            if (notifyKick)
                session.Send(new GenericErrorComposer(GenericError.KickedFromRoom));
            if (nofityUser)
                session.Send(new CloseConnectionComposer());
            if (session.GetHabbo().TentId > 0)
                session.GetHabbo().TentId = 0;
            session.EndCameraContext();
            if (user != null && UsesV2Movement) _room.GetGameMap().Navigation!.Remove(user);
            if (user != null) _room.GetWired()?.BeforeActorLeaves(user);
            session.GetHabbo().CurrentRoom = null;
            if (user != null)
            {
                if (user.RidingHorse)
                {
                    user.RidingHorse = false;
                    var userRiding = GetRoomUserByVirtualId(user.HorseId);
                    if (userRiding != null)
                    {
                        userRiding.RidingHorse = false;
                        userRiding.HorseId = 0;
                    }
                }
                if (user.Team != Team.None)
                {
                    var team = _room.GetTeamManagerForFreeze();
                    if (team != null)
                    {
                        team.OnUserLeave(user);
                        user.Team = Team.None;
                        if (session.GetHabbo().Effects.CurrentEffect != 0)
                            session.GetHabbo().Effects.ApplyEffect(0);
                    }
                }
                removedUser = user;
                RemoveRoomUser(user, actorLeavePrepared: true);
                if (user.CurrentItemEffect != ItemEffectType.None)
                {
                    if (session.GetHabbo().Effects != null)
                        session.GetHabbo().Effects.CurrentEffect = -1;
                }
                if (user.IsTrading)
                {
                    Trade? trade = null;
                    if (_room.GetTrading().TryGetTrade(user.TradeId, out trade))
                        trade.EndTrade(user.TradeId);
                }

                //Session.GetHabbo().CurrentRoomId = 0;
                    session.GetHabbo().Messenger?.NotifyChangesToFriends();
                _store.RecordExit(_room.RoomId, session.GetHabbo().Id, _clock.GetUtcNow(), _room.UsersNow);
            }
        }
        catch (Exception e)
        {
            ExceptionLogger.LogException(e);
        }
        finally
        {
            removedUser?.Dispose();
        }
    }

    private void OnRemove(RoomUser user)
    {
        try
        {
            var session = user.GetClient();
            if (session == null)
                return;
            var bots = new List<RoomUser>();
            try
            {
                foreach (var roomUser in GetUserList().ToList())
                {
                    if (roomUser == null)
                        continue;
                    if (roomUser.IsBot && !roomUser.IsPet)
                    {
                        if (!bots.Contains(roomUser))
                            bots.Add(roomUser);
                    }
                }
            }
            catch { }
            var petsToRemove = new List<RoomUser>();
            foreach (var bot in bots.ToList())
            {
                if (bot == null || bot.BotAi == null)
                    continue;
                bot.BotAi.OnUserLeaveRoom(session);
                if (bot.IsPet && bot.PetData.OwnerId == user.UserId && !_room.CheckRights(session, true))
                {
                    if (!petsToRemove.Contains(bot))
                        petsToRemove.Add(bot);
                }
            }
            foreach (var toRemove in petsToRemove.ToList())
            {
                if (toRemove == null)
                    continue;
                if (session == null || session.GetHabbo() == null || session.GetHabbo().Inventory == null)
                    continue;
                if (session.GetHabbo().Inventory.Pets.AddPet(toRemove.PetData))
                {
                    toRemove.PetData.RoomId = 0;
                    toRemove.PetData.PlacedInRoom = false;
                    RemoveBot(toRemove.VirtualId, false);
                }
            }
            _room.GetGameMap().RemoveUserFromMap(user, new(user.X, user.Y));
        }
        catch (Exception e)
        {
            ExceptionLogger.LogCriticalException(e);
        }
    }

    private bool RemoveRoomUser(RoomUser user, bool detachAfterRemoval = false, bool actorLeavePrepared = false)
    {
        if (!_users.TryGetValue(user.InternalRoomId, out var registered) || !ReferenceEquals(registered, user))
            return false;
        var recipients = GetRoomUsers().Select(roomUser => roomUser.GetClient()).Where(client => client != null)
            .Cast<GameClient>().ToArray();
        if (!_users.TryRemove(new KeyValuePair<int, RoomUser>(user.InternalRoomId, user)))
            return false;
        try
        {
            if (!actorLeavePrepared && _room.GetGameMap().Navigation is { UsesExecutor: true } navigation) navigation.Remove(user);
            if (!actorLeavePrepared)
                _room.GetWired()?.BeforeActorLeaves(user);
            if (!user.IsBot || !user.BotData.IsTemporary)
            {
                if (user.SetStep)
                    _room.GetGameMap().GameMap[user.SetX, user.SetY] = user.SqState;
                else
                    _room.GetGameMap().GameMap[user.X, user.Y] = user.SqState;
            }
            _room.GetGameMap().RemoveUserFromMap(user, new(user.X, user.Y));
            try
            {
                GameClient.SendBroadcast(new UserRemoveComposer(user.VirtualId), recipients);
            }
            catch (Exception error)
            {
                ExceptionLogger.LogException(error);
            }
            _room.GetWired()?.Dispatch(new WiredRuntimeEvent(WiredEventKind.Leave) { Actor = user });
            user.InternalRoomId = -1;
            OnRemove(user);
            return true;
        }
        finally
        {
            if (detachAfterRemoval)
                user.Dispose();
        }
    }

    private void RemoveAbandonedRoomUser(RoomUser user)
    {
        if (RemoveRoomUser(user, true))
            return;
        if (!_users.Values.Any(candidate => ReferenceEquals(candidate, user)))
            user.Dispose();
    }

    public bool TryGetPet(int petId, [NotNullWhen(true)] out RoomUser? pet) => _pets.TryGetValue(petId, out pet);

    public bool TryGetBot(int botId, [NotNullWhen(true)] out RoomUser? bot) => _bots.TryGetValue(botId, out bot);

    public RoomUser? GetBotByName(string name)
    {
        var foundBot = _bots.Count(x => x.Value.BotData != null && x.Value.BotData.Name.ToLower() == name.ToLower()) > 0;
        if (foundBot)
        {
            var id = _bots.FirstOrDefault(x => x.Value.BotData != null && x.Value.BotData.Name.ToLower() == name.ToLower()).Value.BotData.Id;
            return _bots[id];
        }
        return null;
    }

    public void UpdateUserCount(int count)
    {
        UserCount = count;
        _room.UsersNow = count;
        _store.UpdateUserCount(_room.RoomId, count);
    }

    public RoomUser? GetRoomUserByVirtualId(int virtualId)
    {
        RoomUser? user = null;
        if (!_users.TryGetValue(virtualId, out user))
            return null;
        return user;
    }

    public RoomUser? GetRoomUserByHabbo(int id)
    {
        var user = GetUserList().FirstOrDefault(x => !x.IsBot && x.HabboId == id);
        if (user != null)
            return user;
        return null;
    }

    public List<RoomUser> GetRoomUsers()
    {
        var users = new List<RoomUser>();
        foreach (var entry in _users)
        {
            if (!entry.Value.IsBot)
                users.Add(entry.Value);
        }
        return users;
    }

    public List<RoomUser> GetRoomUsersWithPermission(string permission)
    {
        var returnList = new List<RoomUser>();
        foreach (var user in GetUserList().ToList())
        {
            if (user == null)
                continue;
            if (!user.IsBot && user.GetClient()?.GetHabbo()?.Access.Can(permission) == true)
                returnList.Add(user);
        }
        return returnList;
    }

    public RoomUser? GetRoomUserByHabbo(string pName)
    {
        var user = GetUserList().FirstOrDefault(x =>
            x.GetClient()?.GetHabbo()?.Username.Equals(pName, StringComparison.OrdinalIgnoreCase) == true);
        if (user != null)
            return user;
        return null;
    }

    public void UpdatePets()
    {
        foreach (var pet in GetPets().ToList())
        {
            if (pet == null || pet.PetId <= 0)
                continue;
            if (pet.DbState == PetDatabaseUpdateState.NeedsInsert)
            {
                _store.SavePet(new(pet.PetId, pet.OwnerId, pet.RoomId, pet.Name, pet.Type, pet.Race, pet.Color,
                    pet.CreatedAt, 0, 0, 0, 0, 100, 0, 0, true));
            }
            else if (pet.DbState == PetDatabaseUpdateState.NeedsUpdate)
            {
                //Surely this can be *99 better? // TODO
                var user = GetRoomUserByVirtualId(pet.VirtualId);
                _store.SavePet(new(pet.PetId, pet.OwnerId, pet.RoomId, pet.Name, pet.Type, pet.Race, pet.Color,
                    pet.CreatedAt, user?.X ?? 0, user?.Y ?? 0, user?.Z ?? 0, pet.Experience, pet.Energy, pet.Nutrition, pet.Respect, false));
            }
            pet.DbState = PetDatabaseUpdateState.Updated;
        }
    }

    private void UpdateBots()
    {
        foreach (var user in GetRoomUsers().ToList())
        {
            if (user == null || !user.IsBot || user.BotData.IsTemporary)
                continue;
            if (user.IsBot)
            {
                _store.SaveBot(new(user.BotData.BotId, user.X, user.Y, user.Z, user.BotData.Name, user.BotData.Look, user.BotData.Rot));
            }
        }
    }


    public List<Pet> GetPets()
    {
        var pets = new List<Pet>();
        foreach (var user in _pets.Values.ToList())
        {
            if (user == null || !user.IsPet)
                continue;
            pets.Add(user.PetData);
        }
        return pets;
    }

    public void SerializeStatusUpdates()
    {
        var users = new List<RoomUser>();
        var roomUsers = GetUserList();
        if (roomUsers == null)
            return;
        foreach (var user in roomUsers)
        {
            if (user == null || !user.UpdateNeeded)
                continue;
            user.UpdateNeeded = false;
            users.Add(user);
        }
        if (users.Count > 0)
            _room.SendPacket(new UserUpdateComposer(RoomUserStatusSnapshot.Capture(users)));
    }

    public void UpdateUserStatusses()
    {
        if (UsesV2Movement)
        {
            _room.GetGameMap().Navigation!.RefreshPostures();
            return;
        }
        foreach (var user in GetUserList())
        {
            if (user == null)
                continue;
            UpdateUserStatus(user, false);
        }
    }

    private bool IsValid(RoomUser? user)
    {
        if (user == null || _disposed || !user.IsAttachedTo(_room)) return false;
        if (user.IsBot) return true;
        return user.GetClient()?.GetHabbo()?.CurrentRoom == _room;
    }

    internal bool ValidateMovementActor(RoomUser actor)
    {
        if (_disposed)
            return false;
        if (IsValid(actor) && !actor.NeedsAutokick) return true;
        var client = actor.GetClient();
        if (client?.GetHabbo()?.CurrentRoom == _room) RemoveUserFromRoom(client, true);
        else
        {
            RemoveAbandonedRoomUser(actor);
        }
        return false;
    }

    public void OnCycle()
    {
        lock (_stressSync)
        {
            if (_disposed)
                return;
            ProcessStressBots();
            if (UsesV2Movement) _room.GetGameMap().Navigation!.Executor.Tick();
            else CycleUsers();
        }
    }

    private void CycleUsers()
    {
        var userCounter = 0;
        try
        {
            var toRemove = new List<RoomUser>();
            foreach (var user in GetUserList())
            {
                if (user == null)
                    continue;
                if (!IsValid(user))
                {
                    if (user.GetClient() != null)
                        RemoveUserFromRoom(user.GetClient(), false);
                    else
                    {
                        RemoveAbandonedRoomUser(user);
                    }
                }
                if (user.NeedsAutokick && !toRemove.Contains(user))
                {
                    toRemove.Add(user);
                    continue;
                }
                var updated = false;
                user.IdleTime++;
                user.HandleSpamTicks();
                if (!user.IsBot && !user.IsAsleep && user.IdleTime >= 600)
                {
                    user.IsAsleep = true;
                    _room.SendPacket(new SleepComposer(user.VirtualId, true));
                }
                if (user.CarryItemId > 0)
                {
                    user.CarryTimer--;
                    if (user.CarryTimer <= 0)
                        user.CarryItem(0);
                }
                if (_room.GotFreeze())
                    _room.GetFreeze().CycleUser(user);
                var invalidStep = false;
                if (user.IsRolling)
                {
                    if (user.RollerDelay <= 0)
                    {
                        UpdateUserStatus(user, false);
                        user.IsRolling = false;
                    }
                    else
                        user.RollerDelay--;
                }
                if (user.SetStep)
                {
                    if (_room.GetGameMap().IsValidStep2(user, new(user.X, user.Y), new(user.SetX, user.SetY), user.GoalX == user.SetX && user.GoalY == user.SetY, user.AllowOverride))
                    {
                        if (!user.RidingHorse)
                            _room.GetGameMap().UpdateUserMovement(new(user.Coordinate.X, user.Coordinate.Y), new(user.SetX, user.SetY), user);
                        var coordinatedItems = _room.GetGameMap().GetCoordinatedItems(new(user.X, user.Y));
                        foreach (var item in coordinatedItems.ToList()) item.UserWalksOffFurni(user);
                        if (!user.IsBot)
                        {
                            user.X = user.SetX;
                            user.Y = user.SetY;
                            user.Z = user.SetZ;
                        }
                        else if (user.IsBot && !user.RidingHorse)
                        {
                            user.X = user.SetX;
                            user.Y = user.SetY;
                            user.Z = user.SetZ;
                        }
                        if (!user.IsBot && user.RidingHorse)
                        {
                            var horse = GetRoomUserByVirtualId(user.HorseId);
                            if (horse != null)
                            {
                                horse.X = user.SetX;
                                horse.Y = user.SetY;
                            }
                        }
                        if (user.X == _room.GetGameMap().Model.DoorX && user.Y == _room.GetGameMap().Model.DoorY && !toRemove.Contains(user) && !user.IsBot)
                        {
                            toRemove.Add(user);
                            continue;
                        }
                        var items = _room.GetGameMap().GetCoordinatedItems(new(user.X, user.Y));
                        foreach (var item in items.ToList())
                            item.Interactor.OnWalkOn(user);
                        foreach (var item in items.ToList())
                            item.UserWalksOnFurni(user);
                        UpdateUserStatus(user, true);
                    }
                    else
                        invalidStep = true;
                    user.SetStep = false;
                }
                if (user.PathRecalcNeeded)
                {
                    if (user.Path.Count > 1)
                        user.Path.Clear();
                    var shadow = _room.GetGameMap().Navigation;
                    var legacyStarted = shadow?.Enabled == true ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                    user.Path = PathFinder.FindPath(user, _room.GetGameMap().DiagonalEnabled, _room.GetGameMap(), new(user.X, user.Y), new(user.GoalX, user.GoalY));
                    if (shadow?.Enabled == true)
                        shadow.Compare(user, user.Path, System.Diagnostics.Stopwatch.GetTimestamp() - legacyStarted);
                    if (user.Path.Count > 1)
                    {
                        user.PathStep = 1;
                        user.IsWalking = true;
                        user.PathRecalcNeeded = false;
                    }
                    else
                    {
                        user.PathRecalcNeeded = false;
                        if (user.Path.Count > 1)
                            user.Path.Clear();
                    }
                }
                if (user.IsWalking && !user.Freezed)
                {
                    if (invalidStep || user.PathStep >= user.Path.Count || user.GoalX == user.X && user.GoalY == user.Y) //No path found, or reached goal (:
                    {
                        user.IsWalking = false;
                        user.RemoveStatus("mv");
                        if (user.Statusses.ContainsKey("sign"))
                            user.RemoveStatus("sign");
                        if (user.IsBot && user.BotData.TargetUser > 0)
                        {
                            if (user.CarryItemId > 0)
                            {
                                var target = _room.GetRoomUserManager().GetRoomUserByHabbo(user.BotData.TargetUser);
                                if (target != null && Gamemap.TilesTouching(user.X, user.Y, target.X, target.Y))
                                {
                                    user.SetRot(Rotation.Calculate(user.X, user.Y, target.X, target.Y), false);
                                    target.SetRot(Rotation.Calculate(target.X, target.Y, user.X, user.Y), false);
                                    target.CarryItem(user.CarryItemId);
                                }
                            }
                            user.CarryItem(0);
                            user.BotData.TargetUser = 0;
                        }
                        if (user.RidingHorse && user.IsPet == false && !user.IsBot)
                        {
                            var mascotaVinculada = GetRoomUserByVirtualId(user.HorseId);
                            if (mascotaVinculada != null)
                            {
                                mascotaVinculada.IsWalking = false;
                                mascotaVinculada.RemoveStatus("mv");
                                mascotaVinculada.UpdateNeeded = true;
                            }
                        }
                    }
                    else
                    {
                        var nextStep = user.Path[user.Path.Count - user.PathStep - 1];
                        user.PathStep++;
                        if (user.FastWalking && user.PathStep < user.Path.Count)
                        {
                            var s2 = user.Path.Count - user.PathStep - 1;
                            nextStep = user.Path[s2];
                            user.PathStep++;
                        }
                        if (user.SuperFastWalking && user.PathStep < user.Path.Count)
                        {
                            var s2 = user.Path.Count - user.PathStep - 1;
                            nextStep = user.Path[s2];
                            user.PathStep++;
                            user.PathStep++;
                        }
                        var nextX = nextStep.X;
                        var nextY = nextStep.Y;
                        user.RemoveStatus("mv");
                        if (_room.GetGameMap().IsValidStep2(user, new(user.X, user.Y), new(nextX, nextY), user.GoalX == nextX && user.GoalY == nextY, user.AllowOverride))
                        {
                            var nextZ = _room.GetGameMap().SqAbsoluteHeight(nextX, nextY);
                            if (!user.IsBot)
                            {
                                if (user.IsSitting)
                                {
                                    user.Statusses.Remove("sit");
                                    user.Z += 0.35;
                                    user.IsSitting = false;
                                    user.UpdateNeeded = true;
                                }
                                else if (user.IsLying)
                                {
                                    user.Statusses.Remove("sit");
                                    user.Z += 0.35;
                                    user.IsLying = false;
                                    user.UpdateNeeded = true;
                                }
                            }
                            if (!user.IsBot)
                            {
                                user.Statusses.Remove("lay");
                                user.Statusses.Remove("sit");
                            }
                            if (!user.IsBot && !user.IsPet && user.GetClient() != null)
                            {
                                if (user.GetClient().GetHabbo().IsTeleporting)
                                {
                                    user.GetClient().GetHabbo().IsTeleporting = false;
                                    user.GetClient().GetHabbo().TeleporterId = 0;
                                }
                                else if (user.GetClient().GetHabbo().IsHopping)
                                {
                                    user.GetClient().GetHabbo().IsHopping = false;
                                    user.GetClient().GetHabbo().HopperId = 0;
                                }
                            }
                            if (!user.IsBot && user.RidingHorse && user.IsPet == false)
                            {
                                var horse = GetRoomUserByVirtualId(user.HorseId);
                                if (horse != null)
                                    horse.SetStatus("mv", $"{nextX},{nextY},{TextHandling.GetString(nextZ)}");
                                var riderZ = _room.GetGameMap().WalkMagicAt(nextX, nextY) == null ? nextZ + 1 : nextZ;
                                user.SetStatus("mv", $"{nextX},{nextY},{TextHandling.GetString(riderZ)}");
                                user.UpdateNeeded = true;
                                horse.UpdateNeeded = true;
                            }
                            else
                                user.SetStatus("mv", $"{nextX},{nextY},{TextHandling.GetString(nextZ)}");
                            var newRot = Rotation.Calculate(user.X, user.Y, nextX, nextY, user.MoonwalkEnabled);
                            user.RotBody = newRot;
                            user.RotHead = newRot;
                            user.SetStep = true;
                            user.SetX = nextX;
                            user.SetY = nextY;
                            user.SetZ = nextZ;
                            UpdateUserEffect(user, user.SetX, user.SetY);
                            updated = true;
                            if (user.RidingHorse && user.IsPet == false && !user.IsBot)
                            {
                                var horse = GetRoomUserByVirtualId(user.HorseId);
                                if (horse != null)
                                {
                                    horse.RotBody = newRot;
                                    horse.RotHead = newRot;
                                    horse.SetStep = true;
                                    horse.SetX = nextX;
                                    horse.SetY = nextY;
                                    horse.SetZ = nextZ;
                                }
                            }
                            // Stress bots overlap without owning a walkability-grid reservation.
                            if (!user.IsBot || !user.BotData.IsTemporary)
                            {
                                _room.GetGameMap().GameMap[user.X, user.Y] = user.SqState; // REstore the old one
                                user.SqState = _room.GetGameMap().GameMap[user.SetX, user.SetY]; //Backup the new one
                                if (!_room.RoomBlockingEnabled)
                                {
                                    var users = _room.GetRoomUserManager().GetUserForSquare(nextX, nextY);
                                    if (users != null)
                                        _room.GetGameMap().GameMap[nextX, nextY] = 0;
                                }
                                else
                                    _room.GetGameMap().GameMap[nextX, nextY] = 1;
                            }
                        }
                    }
                    if (!user.RidingHorse)
                        user.UpdateNeeded = true;
                }
                else
                {
                    if (user.Statusses.ContainsKey("mv"))
                    {
                        user.RemoveStatus("mv");
                        user.UpdateNeeded = true;
                        if (user.RidingHorse)
                        {
                            var horse = GetRoomUserByVirtualId(user.HorseId);
                            if (horse != null)
                            {
                                horse.RemoveStatus("mv");
                                horse.UpdateNeeded = true;
                            }
                        }
                    }
                }
                if (user.RidingHorse)
                    user.ApplyEffect(77);
                if (user.IsBot && user.BotAi != null)
                    user.BotAi.OnTimerTick();
                else
                    userCounter++;
                if (!updated) UpdateUserEffect(user, user.X, user.Y);
            }
            foreach (var userToRemove in toRemove.ToList())
            {
                var client = userToRemove.GetClient();
                if (client != null)
                    RemoveUserFromRoom(client, true);
                else
                {
                    RemoveAbandonedRoomUser(userToRemove);
                }
            }
            if (UserCount != userCounter)
                UpdateUserCount(userCounter);
        }
        catch (Exception e)
        {
            ExceptionLogger.LogCriticalException(e);
        }
    }

    public void UpdateUserStatus(RoomUser user, bool cyclegameitems)
    {
        if (user == null)
            return;
        var wasLaying = user.Statusses.ContainsKey("lay");
        try
        {
            var isBot = user.IsBot;
            if (isBot)
                cyclegameitems = false;
            UpdateSignStatus(user);
            var itemsOnSquare = _room.GetGameMap().GetAllRoomItemForSquare(user.X, user.Y);
            var model = _room.GetGameMap().Model;
            var walkMagic = _room.GetGameMap().WalkMagicAt(user.X, user.Y);
            if (walkMagic != null)
            {
                // Height rebinding is callback-free; movement still dispatches the legacy furni hooks.
                user.Statusses.Remove("sit");
                user.Statusses.Remove("lay");
                user.IsSitting = false;
                user.IsLying = false;
                user.Z = walkMagic.GetZ;
                user.UpdateNeeded = true;
                if (!cyclegameitems) return;
            }
            var hasSeat = walkMagic == null && (model.SqState[user.X, user.Y] == SquareState.Seat || itemsOnSquare.Any(squareItem => squareItem?.Definition?.IsSeat == true));
            var hasBed = walkMagic == null && itemsOnSquare.Any(squareItem => squareItem?.Definition?.InteractionType is InteractionType.Bed or InteractionType.TentSmall);
            if (RoomPosture.ReleaseSit(user.IsSitting, user.Statusses.ContainsKey("sit"), hasSeat))
            {
                user.Statusses.Remove("sit");
                user.UpdateNeeded = true;
            }
            if (RoomPosture.ReleaseLay(user.IsLying, user.Statusses.ContainsKey("lay"), hasBed))
            {
                user.Statusses.Remove("lay");
                user.UpdateNeeded = true;
            }
            if (user.IsLying || user.IsSitting)
                return;
            double newZ;
            if (itemsOnSquare != null || itemsOnSquare.Count != 0)
            {
                if (walkMagic == null && user.RidingHorse && user.IsPet == false)
                    newZ = _room.GetGameMap().SqAbsoluteHeight(user.X, user.Y, itemsOnSquare.ToList()) + 1;
                else
                    newZ = _room.GetGameMap().SqAbsoluteHeight(user.X, user.Y, itemsOnSquare.ToList());
            }
            else
                newZ = 1;
            if (newZ != user.Z)
            {
                user.Z = newZ;
                user.UpdateNeeded = true;
            }
            if (walkMagic == null && model.SqState[user.X, user.Y] == SquareState.Seat)
            {
                if (!user.Statusses.ContainsKey("sit"))
                    user.Statusses.Add("sit", "1.0");
                user.Z = model.SqFloorHeight[user.X, user.Y];
                user.RotHead = model.SqSeatRot[user.X, user.Y];
                user.RotBody = model.SqSeatRot[user.X, user.Y];
                user.UpdateNeeded = true;
            }
            if (itemsOnSquare.Count == 0)
                user.LastItem = null;
            foreach (var item in itemsOnSquare.ToList())
            {
                if (item == null)
                    continue;
                if (walkMagic == null && item.Definition.IsSeat)
                {
                    if (!user.Statusses.ContainsKey("sit"))
                    {
                        if (!user.Statusses.ContainsKey("sit"))
                            user.Statusses.Add("sit", TextHandling.GetString(item.Definition.Height));
                    }
                    user.Z = item.GetZ;
                    user.RotHead = item.Rotation;
                    user.RotBody = item.Rotation;
                    user.UpdateNeeded = true;
                }
                switch (item.Definition.InteractionType)
                {
                    case InteractionType.Bed:
                    case InteractionType.TentSmall:
                        {
                            if (walkMagic != null) break;
                            if (!user.Statusses.ContainsKey("lay"))
                                user.Statusses.Add("lay", $"{TextHandling.GetString(item.Definition.Height)} null");
                            user.Z = item.GetZ;
                            user.RotHead = item.Rotation;
                            user.RotBody = item.Rotation;
                            user.UpdateNeeded = true;
                            break;
                        }
                    case InteractionType.Banzaigategreen:
                    case InteractionType.Banzaigateblue:
                    case InteractionType.Banzaigatered:
                    case InteractionType.Banzaigateyellow:
                        {
                            if (cyclegameitems)
                            {
                                var effectId = Convert.ToInt32(item.Team + 32);
                                var t = user.GetClient().GetHabbo().CurrentRoom.GetTeamManagerForBanzai();
                                if (user.Team == Team.None)
                                {
                                    if (t.CanEnterOnTeam(item.Team))
                                    {
                                        if (user.Team != Team.None)
                                            t.OnUserLeave(user);
                                        user.Team = item.Team;
                                        t.AddUser(user);
                                        if (user.GetClient().GetHabbo().Effects.CurrentEffect != effectId)
                                            user.GetClient().GetHabbo().Effects.ApplyEffect(effectId);
                                    }
                                }
                                else if (user.Team != Team.None && user.Team != item.Team)
                                {
                                    t.OnUserLeave(user);
                                    user.Team = Team.None;
                                    user.GetClient().GetHabbo().Effects.ApplyEffect(0);
                                }
                                else
                                {
                                    //usersOnTeam--;
                                    t.OnUserLeave(user);
                                    if (user.GetClient().GetHabbo().Effects.CurrentEffect == effectId)
                                        user.GetClient().GetHabbo().Effects.ApplyEffect(0);
                                    user.Team = Team.None;
                                }
                                //Item.ExtraData = usersOnTeam.ToString();
                                //Item.UpdateState(false, true);
                            }
                            break;
                        }
                    case InteractionType.FreezeYellowGate:
                    case InteractionType.FreezeRedGate:
                    case InteractionType.FreezeGreenGate:
                    case InteractionType.FreezeBlueGate:
                        {
                            if (cyclegameitems)
                            {
                                var effectId = Convert.ToInt32(item.Team + 39);
                                var t = user.GetClient().GetHabbo().CurrentRoom.GetTeamManagerForFreeze();
                                if (user.Team == Team.None)
                                {
                                    if (t.CanEnterOnTeam(item.Team))
                                    {
                                        if (user.Team != Team.None)
                                            t.OnUserLeave(user);
                                        user.Team = item.Team;
                                        t.AddUser(user);
                                        if (user.GetClient().GetHabbo().Effects.CurrentEffect != effectId)
                                            user.GetClient().GetHabbo().Effects.ApplyEffect(effectId);
                                    }
                                }
                                else if (user.Team != Team.None && user.Team != item.Team)
                                {
                                    t.OnUserLeave(user);
                                    user.Team = Team.None;
                                    user.GetClient().GetHabbo().Effects.ApplyEffect(0);
                                }
                                else
                                {
                                    //usersOnTeam--;
                                    t.OnUserLeave(user);
                                    if (user.GetClient().GetHabbo().Effects.CurrentEffect == effectId)
                                        user.GetClient().GetHabbo().Effects.ApplyEffect(0);
                                    user.Team = Team.None;
                                }
                                //Item.ExtraData = usersOnTeam.ToString();
                                //Item.UpdateState(false, true);
                            }
                            break;
                        }
                    case InteractionType.Banzaitele:
                        {
                            if (user.Statusses.ContainsKey("mv"))
                                _room.GetGameItemHandler().OnTeleportRoomUserEnter(user, item);
                            break;
                        }
                    case InteractionType.Effect:
                        {
                            if (user == null)
                                return;
                            if (!user.IsBot)
                            {
                                if (item == null || item.Definition == null || user.GetClient() == null || user.GetClient().GetHabbo() == null || user.GetClient().GetHabbo().Effects == null)
                                    return;
                                if (item.Definition.EffectId == 0 && user.GetClient().GetHabbo().Effects.CurrentEffect == 0)
                                    return;
                                user.GetClient().GetHabbo().Effects.ApplyEffect(item.Definition.EffectId);
                                item.LegacyDataString = "1";
                                item.UpdateState(false, true);
                                item.RequestUpdate(2, true);
                            }
                            break;
                        }
                    case InteractionType.Arrow:
                        {
                            if (user.GoalX == item.GetX && user.GoalY == item.GetY)
                            {
                                if (user == null || user.GetClient() == null || user.GetClient().GetHabbo() == null)
                                    continue;
                                var room = user.GetClient().GetHabbo().CurrentRoom;
                                if (room == null)
                                    return;
                                if (!ItemTeleporterFinder.IsTeleLinked(item.Id, room, _travelStore))
                                    user.UnlockWalking();
                                else
                                {
                                    var linkedTele = _travelStore.FindLinkedTeleporter(item.Id);
                                    var teleRoomId = ItemTeleporterFinder.GetTeleRoomId(linkedTele, room, _travelStore);
                                    if (teleRoomId == room.RoomId)
                                    {
                                        var targetItem = room.GetRoomItemHandler().GetItem(linkedTele);
                                        if (targetItem == null)
                                        {
                                            if (user.GetClient() != null)
                                                user.GetClient().SendWhisper("Hey, that arrow is poorly!");
                                            return;
                                        }
                                        room.GetGameMap().TeleportToItem(user, targetItem);
                                    }
                                    else if (teleRoomId != room.RoomId)
                                    {
                                        if (user != null && !user.IsBot && user.GetClient() != null && user.GetClient().GetHabbo() != null)
                                        {
                                            user.GetClient().GetHabbo().IsTeleporting = true;
                                            user.GetClient().GetHabbo().TeleportingRoomId = teleRoomId;
                                            user.GetClient().GetHabbo().TeleporterId = linkedTele;
                                            user.GetClient().GetHabbo().PrepareRoom(teleRoomId, "");
                                        }
                                    }
                                    else if (_room.GetRoomItemHandler().GetItem(linkedTele) != null)
                                    {
                                        user.SetPos(item.GetX, item.GetY, item.GetZ);
                                        user.SetRot(item.Rotation, false);
                                    }
                                    else
                                        user.UnlockWalking();
                                }
                            }
                            break;
                        }
                }
            }
            if (user.IsSitting && user.TeleportEnabled)
            {
                user.Z -= 0.35;
                user.UpdateNeeded = true;
            }
            if (cyclegameitems)
            {
                if (_room.GotSoccer())
                    _room.GetSoccer().OnUserWalk(user);
                if (_room.GotBanzai())
                    _room.GetBanzai().OnUserWalk(user);
                if (_room.GotFreeze())
                    _room.GetFreeze().OnUserWalk(user);
            }
        }
        catch (Exception e)
        {
            ExceptionLogger.LogException(e);
        }
        finally
        {
            if (!wasLaying && user.Statusses.ContainsKey("lay"))
                _room.GetWired().Dispatch(new(WiredEventKind.AvatarAction) { Actor = user, Action = (int)WiredAvatarAction.Lay });
        }
    }

    internal void UpdateSignStatus(RoomUser user)
    {
        if (user.SignExpiresAt is not { } deadline)
            return;
        var now = _clock.GetUtcNow();
        if (now < deadline)
            return;
        user.SignExpiresAt = null;
        user.Statusses.Remove("sign");
        user.UpdateNeeded = true;
    }

    private void UpdateUserEffect(RoomUser user, int x, int y)
    {
        if (user == null || user.IsBot)
            return;
        var client = user.GetClient();
        var habbo = client?.GetHabbo();
        if (habbo == null) return;
        try
        {
            var newCurrentUserItemEffect = _room.GetGameMap().EffectMap[x, y];
            if (newCurrentUserItemEffect > 0)
            {
                if (habbo.Effects.CurrentEffect == 0)
                    user.CurrentItemEffect = ItemEffectType.None;
                var type = ByteToItemEffectEnum.Parse(newCurrentUserItemEffect);
                if (type != user.CurrentItemEffect)
                {
                    switch (type)
                    {
                        case ItemEffectType.Iceskates:
                            {
                                habbo.Effects.ApplyEffect(habbo.Gender == "M" ? 38 : 39);
                                user.CurrentItemEffect = ItemEffectType.Iceskates;
                                break;
                            }
                        case ItemEffectType.Normalskates:
                            {
                                habbo.Effects.ApplyEffect(habbo.Gender == "M" ? 55 : 56);
                                user.CurrentItemEffect = type;
                                break;
                            }
                        case ItemEffectType.Swim:
                            {
                                habbo.Effects.ApplyEffect(29);
                                user.CurrentItemEffect = type;
                                _rewards.Progress(client, RewardTrackActions.Swim);
                                break;
                            }
                        case ItemEffectType.SwimLow:
                            {
                                habbo.Effects.ApplyEffect(30);
                                user.CurrentItemEffect = type;
                                break;
                            }
                        case ItemEffectType.SwimHalloween:
                            {
                                habbo.Effects.ApplyEffect(37);
                                user.CurrentItemEffect = type;
                                break;
                            }
                        case ItemEffectType.None:
                            {
                                habbo.Effects.ApplyEffect(-1);
                                user.CurrentItemEffect = type;
                                break;
                            }
                    }
                }
            }
            else if (user.CurrentItemEffect != ItemEffectType.None && newCurrentUserItemEffect == 0)
            {
                habbo.Effects.ApplyEffect(-1);
                user.CurrentItemEffect = ItemEffectType.None;
            }
        }
        catch { }
    }

    public ICollection<RoomUser> GetUserList() => _users.Values;

    public void Dispose()
    {
        if (_room?.UsesV2Movement == true)
        {
            DisposeExecutorUsers();
            return;
        }
        lock (_stressSync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _stressRequests.Clear();
            DisposeUsers();
        }
    }

    private void DisposeExecutorUsers()
    {
        var room = _room;
        if (room == null) return;
        // Match the tick's room-owner -> stress-request lock order.
        lock (room.NavigationSync)
        lock (_stressSync)
        {
            if (_disposed) return;
            _disposed = true;
            _stressRequests.Clear();
            DisposeUsers();
        }
    }

    private void DisposeUsers()
    {
        var users = _users.Values.ToArray();
        try
        {
            if (UsesV2Movement) _room.GetGameMap()?.Navigation?.Shutdown();
            foreach (var user in users) _room.GetWired()?.BeforeActorLeaves(user);
            UpdatePets();
            UpdateBots();
            _room.UsersNow = 0;
            _store.UpdateUserCount(_room.Id, 0);
        }
        finally
        {
            _users.Clear();
            _pets.Clear();
            _bots.Clear();
            UserCount = 0;
            PetCount = 0;
            foreach (var user in users)
            {
                user.BotAi?.Detach(_room, user);
                user.PetData?.Detach(_room);
                user.Dispose();
            }
            _users = null;
            _pets = null;
            _bots = null;
            _room = null;
        }
    }
}
