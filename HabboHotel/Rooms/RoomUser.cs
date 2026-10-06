using Plus.HabboHotel.Permissions;
using System.Drawing;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Rooms.Avatar;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.Chat.Emotions;
using Plus.HabboHotel.Rooms.Games.Freeze;
using Plus.HabboHotel.Rooms.Games.Teams;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms;

public class RoomUser
{
    internal ActorProfile? NavigationProfile { get; set; }
    private ActorMovementState? _movement;
    public ActorMovementState Movement => LazyInitializer.EnsureInitialized(ref _movement)!;

    public WiredRoomEntrySnapshot WiredRoomEntry { get; internal set; }
    private GameClient? _mClient;
    private Room? _mRoom;
    private readonly IChatEmotionsManager _chatEmotions;
    private readonly IRewardTrackManager _rewards;

    public bool AllowOverride;

    public FreezePowerUp BanzaiPowerUp;
    public BotAi BotAi;
    public RoomBot BotData;
    public bool CanWalk;
    public int CarryItemId; //byte
    public int CarryTimer; //byte
    public int ChatSpamCount;
    public int ChatSpamTicks = 16;
    public ItemEffectType CurrentItemEffect;
    public int DanceId;
    public bool FastWalking = false;
    public int FreezeCounter;
    public bool Freezed;
    public bool FreezeInteracting;
    public int FreezeLives;
    public bool Frozen;
    public uint GateId;

    public int GoalX; //byte
    public int GoalY; //byte
    public int HabboId;
    public int HorseId = 0;
    public int IdleTime; //byte
    public bool InteractingGate;
    public int InternalRoomId;
    public bool IsAsleep;
    public bool IsJumping;
    public bool IsLying = false;

    public bool IsRolling = false;
    public bool IsSitting = false;
    public bool IsWalking;
    public int LastBubble = 0;
    public DateTimeOffset? LastInteractionAt;
    public Item LastItem = null;

    public int LlPartner = 0;
    public int LockedTilesCount;
    public bool MoonwalkEnabled = false;

    public List<Vector2D> Path = new();
    public bool PathRecalcNeeded;
    public int PathStep = 1;
    public Pet PetData;

    public int PrevTime;
    public bool RidingHorse = false;
    public int RollerDelay = 0;
    public uint RoomId;
    public int RotBody; //byte
    public int RotHead; //byte

    public bool SetStep;
    public int SetX; //byte
    public int SetY; //byte
    public double SetZ;
    public bool ShieldActive;
    public int ShieldCounter;
    public DateTimeOffset? SignExpiresAt;
    public byte SqState;
    public bool SuperFastWalking = false;
    public Team Team;
    public int TeleDelay; //byte
    public bool TeleportEnabled;
    public double TimeInRoom;
    public bool UpdateNeeded;
    public int UserId;
    public int VirtualId;

    public int X; //byte
    public int Y; //byte
    public double Z;

    public RoomUser(int habboId, uint roomId, int virtualId, Room room, GameClient? client,
        IChatEmotionsManager chatEmotions, IRewardTrackManager rewards)
    {
        Freezed = false;
        HabboId = habboId;
        RoomId = roomId;
        VirtualId = virtualId;
        IdleTime = 0;
        X = 0;
        Y = 0;
        Z = 0;
        PrevTime = 0;
        RotHead = 0;
        RotBody = 0;
        UpdateNeeded = true;
        Statusses = new();
        TeleDelay = -1;
        _mRoom = room;
        _mClient = client;
        _chatEmotions = chatEmotions;
        _rewards = rewards;
        AllowOverride = false;
        CanWalk = true;
        SqState = 3;
        InternalRoomId = 0;
        CurrentItemEffect = ItemEffectType.None;
        FreezeLives = 0;
        InteractingGate = false;
        GateId = 0;
        LastInteractionAt = null;
        LockedTilesCount = 0;
        IsJumping = false;
        TimeInRoom = 0;
        TradeId = 0;
        TradePartner = 0;
        IsTrading = false;
    }


    public Point Coordinate => new(X, Y);

    public bool IsPet => IsBot && BotData.IsPet;

    public int CurrentEffect => GetClient()?.GetHabbo()?.Effects.CurrentEffect ?? 0;


    public bool IsDancing
    {
        get
        {
            if (DanceId >= 1) return true;
            return false;
        }
    }

    public bool IsTrading { get; set; }

    public int TradePartner { get; set; }

    public int TradeId { get; set; }


    public Dictionary<string, string> Statusses { get; }
    //set { this._statusses = value; }

    public bool NeedsAutokick
    {
        get
        {
            if (IsBot)
                return false;
            if (GetClient() == null || GetClient().GetHabbo() == null)
                return true;
            if (GetClient().GetHabbo().Access.Can(PermissionKeys.ModerationTool) || GetRoom().OwnerId == HabboId)
                return false;
            if (GetRoom().Id == 1649919)
                return false;
            if (IdleTime >= 7200)
                return true;
            return false;
        }
    }

    public bool IsBot
    {
        get
        {
            if (BotData != null)
                return true;
            return false;
        }
    }

    public Point SquareInFront
    {
        get
        {
            var sq = new Point(X, Y);
            if (RotBody == 0)
                sq.Y--;
            else if (RotBody == 2)
                sq.X++;
            else if (RotBody == 4)
                sq.Y++;
            else if (RotBody == 6) sq.X--;
            return sq;
        }
    }

    public Point SquareBehind
    {
        get
        {
            var sq = new Point(X, Y);
            if (RotBody == 0)
                sq.Y++;
            else if (RotBody == 2)
                sq.X--;
            else if (RotBody == 4)
                sq.Y--;
            else if (RotBody == 6) sq.X++;
            return sq;
        }
    }

    public Point SquareLeft
    {
        get
        {
            var sq = new Point(X, Y);
            if (RotBody == 0)
                sq.X++;
            else if (RotBody == 2)
                sq.Y--;
            else if (RotBody == 4)
                sq.X--;
            else if (RotBody == 6) sq.Y++;
            return sq;
        }
    }

    public Point SquareRight
    {
        get
        {
            var sq = new Point(X, Y);
            if (RotBody == 0)
                sq.X--;
            else if (RotBody == 2)
                sq.Y++;
            else if (RotBody == 4)
                sq.X++;
            else if (RotBody == 6) sq.Y--;
            return sq;
        }
    }

    public string GetUsername()
    {
        if (IsBot)
            return string.Empty;
        return GetClient()?.GetHabbo()?.Username ?? "Unknown User";
    }

    public void UnIdle()
    {
        var room = GetRoom();
        if (room == null)
            return;
        if (!IsBot)
        {
            var habbo = GetClient()?.GetHabbo();
            if (habbo != null)
                habbo.TimeAfk = 0;
        }
        IdleTime = 0;
        if (IsAsleep)
        {
            IsAsleep = false;
            room.SendPacket(new SleepComposer(VirtualId, false));
            room.GetWired().Dispatch(new(WiredEventKind.AvatarAction) { Actor = this, Action = (int)WiredAvatarAction.Awake });
        }
    }

    public void Dispose()
    {
        Statusses.Clear();
        Interlocked.Exchange(ref _mRoom, null);
        Interlocked.Exchange(ref _mClient, null);
    }

    public void Chat(string message, int colour = 0)
    {
        var room = GetRoom();
        if (room == null || !IsBot)
            return;
        var packet = new ChatComposer(VirtualId, message, 0, IsPet ? 0 : colour == 0 ? 2 : colour);
        GameClient.SendBroadcast(packet, GetRecipients());

        IEnumerable<GameClient> GetRecipients()
        {
            foreach (var user in room.GetRoomUserManager().GetRoomUsers())
            {
                var client = user?.GetClient();
                var habbo = client?.GetHabbo();
                if (client == null || habbo == null)
                    yield break;
                if (!(IsPet ? habbo.AllowPetSpeech : habbo.AllowBotSpeech))
                    yield return client;
            }
        }
    }

    public void HandleSpamTicks()
    {
        if (ChatSpamTicks >= 0)
        {
            ChatSpamTicks--;
            if (ChatSpamTicks == -1) ChatSpamCount = 0;
        }
    }

    public bool IncrementAndCheckFlood(DateTimeOffset now, out int muteTime)
    {
        muteTime = 0;
        var habbo = GetClient()?.GetHabbo();
        if (habbo == null || !ReferenceEquals(habbo.CurrentRoom, GetRoom()))
            return false;
        ChatSpamCount++;
        if (ChatSpamTicks == -1)
            ChatSpamTicks = 8;
        else if (ChatSpamCount >= 6)
        {
            muteTime = Math.Clamp(21 - habbo.Access.Limit("limit.flood_tolerance", 1), 1, 20);
            habbo.FloodUntil = now.AddSeconds(muteTime);
            ChatSpamCount = 0;
            return true;
        }
        return false;
    }

    public void OnChat(int colour, string message, bool shout)
    {
        var room = GetRoom();
        var client = GetClient();
        var habbo = client?.GetHabbo();
        if (room == null || client == null || habbo == null || !ReferenceEquals(habbo.CurrentRoom, room))
            return;
        if (room.GetWired().TriggerEvent(WiredBoxType.TriggerUserSays, habbo, message))
            return;
        habbo.HasSpoken = true;
        if (room.WordFilterList.Count > 0 && !habbo.Access.Can(PermissionKeys.ChatFilterBypass)) message = room.GetFilter().CheckMessage(message);
        IServerPacket? packet = null;
        if (shout)
            packet = new ShoutComposer(VirtualId, message, _chatEmotions.GetEmotionsForText(message), colour);
        else
            packet = new ChatComposer(VirtualId, message, _chatEmotions.GetEmotionsForText(message), colour);
        if (habbo.TentId > 0)
        {
            room.SendToTent(habbo.Id, habbo.TentId, packet);
            packet = new WhisperComposer(VirtualId, $"[Tent Chat] {message}", 0, colour);
            var toNotify = room.GetRoomUserManager().GetRoomUsersWithPermission(PermissionKeys.StaffReceiveAlerts);
            if (toNotify.Count > 0)
            {
                foreach (var user in toNotify)
                {
                    if (user == null || user.GetClient() == null || user.GetClient().GetHabbo() == null ||
                        user.GetClient().GetHabbo().TentId == habbo.TentId)
                        continue;
                    user.GetClient().Send(packet);
                }
            }
        }
        else
        {
            foreach (var user in room.GetRoomUserManager().GetRoomUsers().ToList())
            {
                if (user == null || user.GetClient() == null || user.GetClient().GetHabbo() == null || user.GetClient().GetHabbo().IgnoresComponent.IsIgnored(habbo.Id))
                    continue;
                if (room.ChatDistance > 0 && Gamemap.TileDistance(X, Y, user.X, user.Y) > room.ChatDistance)
                    continue;
                user.GetClient().Send((IServerPacket)packet);
            }
        }
        if (shout)
        {
            foreach (var user in room.GetRoomUserManager().GetUserList().ToList())
            {
                if (!user.IsBot)
                    continue;
                if (user.IsBot)
                    user.BotAi.OnUserShout(this, message);
            }
        }
        else
        {
            foreach (var user in room.GetRoomUserManager().GetUserList().ToList())
            {
                if (!user.IsBot)
                    continue;
                if (user.IsBot)
                    user.BotAi.OnUserSay(this, message);
            }
        }
    }

    public void ClearMovement(bool update)
    {
        var room = GetRoom();
        if (room == null)
            return;
        if (room.GetGameMap()?.Navigation is { UsesExecutor: true } navigation)
        {
            navigation.Cancel(this);
            return;
        }
        IsWalking = false;
        Statusses.Remove("mv");
        GoalX = 0;
        GoalY = 0;
        SetStep = false;
        SetX = 0;
        SetY = 0;
        SetZ = 0;
        if (update) UpdateNeeded = true;
    }

    public void MoveTo(Point c)
    {
        MoveTo(c.X, c.Y);
    }

    public void MoveTo(int pX, int pY, bool pOverride)
    {
        var room = GetRoom();
        if (room == null)
            return;
        if (room.GetGameMap()?.Navigation is { UsesExecutor: true } navigation)
        {
            navigation.Move(this, pX, pY, IsBot ? MoveOrigin.Bot : MoveOrigin.User,
                TeleportEnabled ? MoveFlags.Teleport : MoveFlags.None);
            return;
        }
        if (TeleportEnabled)
        {
            UnIdle();
            room.SendPacket(room.GetRoomItemHandler().UpdateUserOnRoller(this, new(pX, pY), 0, room.GetGameMap().SqAbsoluteHeight(GoalX, GoalY)));
            if (Statusses.ContainsKey("sit"))
                Z -= 0.35;
            UpdateNeeded = true;
            return;
        }
        if (room.GetGameMap().SquareHasUsers(pX, pY) && !pOverride || Frozen)
            return;
        UnIdle();
        GoalX = pX;
        GoalY = pY;
        PathRecalcNeeded = true;
        FreezeInteracting = false;
    }

    public void MoveTo(int pX, int pY)
    {
        MoveTo(pX, pY, false);
    }

    // Walks to the item's approach tile; under v2 with approach_auto_interact the arrival starts the interaction.
    public void ApproachItem(Item item, int actionKind)
    {
        var room = GetRoom();
        if (room == null)
            return;
        var front = item.SquareInFront;
        if (!IsBot && !TeleportEnabled && room.GetGameMap()?.Navigation is { UsesExecutor: true, Settings.ApproachAutoInteract: true } navigation
            && navigation.DescribeApproach(item, actionKind) is { } approach)
            navigation.Move(this, front.X, front.Y, MoveOrigin.User, MoveFlags.None, approach);
        else MoveTo(front);
    }

    public void MoveTo(int x, int y, MoveOrigin origin, MoveFlags flags = MoveFlags.None)
    {
        var room = GetRoom();
        if (room == null)
            return;
        if (room.GetGameMap()?.Navigation is { UsesExecutor: true } navigation)
            navigation.Move(this, x, y, origin, flags);
        else MoveTo(x, y);
    }

    internal void InitializePosition(int x, int y, double z)
    {
        X = x; Y = y; Z = z;
    }

    public void UnlockWalking()
    {
        AllowOverride = false;
        CanWalk = true;
    }


    public void SetPos(int pX, int pY, double pZ)
    {
        var room = GetRoom();
        if (room == null)
            return;
        if (room.GetGameMap()?.Navigation is { UsesExecutor: true } navigation)
        {
            navigation.ForcePlace(this, pX, pY, pZ, ForceResolution.ExactZ);
            return;
        }
        X = pX;
        Y = pY;
        Z = pZ;
    }

    public void CarryItem(int item)
    {
        var room = GetRoom();
        if (room == null)
            return;
        var previous = CarryItemId;
        CarryItemId = item;
        if (item > 0)
            CarryTimer = 240;
        else
            CarryTimer = 0;
        room.SendPacket(new CarryObjectComposer(VirtualId, item));
        var client = GetClient();
        if (item > 0 && item != previous && !IsBot && client != null)
            _rewards.Progress(client, RewardTrackActions.FindHandItem);
    }


    public void SetRot(int rotation, bool headOnly)
    {
        if (Statusses.ContainsKey("lay") || IsWalking) return;
        var diff = RotBody - rotation;
        RotHead = RotBody;
        if (Statusses.ContainsKey("sit") || headOnly)
        {
            if (RotBody == 2 || RotBody == 4)
            {
                if (diff > 0)
                    RotHead = RotBody - 1;
                else if (diff < 0) RotHead = RotBody + 1;
            }
            else if (RotBody == 0 || RotBody == 6)
            {
                if (diff > 0)
                    RotHead = RotBody - 1;
                else if (diff < 0) RotHead = RotBody + 1;
            }
        }
        else if (diff <= -2 || diff >= 2)
        {
            RotHead = rotation;
            RotBody = rotation;
        }
        else
            RotHead = rotation;
        UpdateNeeded = true;
    }


    public bool HasStatus(string key) => Statusses.ContainsKey(key);

    public void RemoveStatus(string key)
    {
        if (HasStatus(key))
            Statusses.Remove(key);
    }

    public void SetStatus(string key, string value = "")
    {
        if (Statusses.ContainsKey(key))
            Statusses[key] = value;
        else
            Statusses.Add(key, value);
    }


    public void ApplyEffect(int effectId)
    {
        var room = GetRoom();
        if (room == null)
            return;
        if (IsBot)
        {
            room.SendPacket(new AvatarEffectComposer(VirtualId, effectId));
            return;
        }
        var effects = GetClient()?.GetHabbo()?.Effects;
        if (effects == null)
            return;
        effects.ApplyEffect(effectId);
    }


    public GameClient? GetClient() => IsBot ? null : _mClient;

    internal bool IsAttachedTo(Room room) => ReferenceEquals(_mRoom, room);

    private Room? GetRoom() => _mRoom;
}
