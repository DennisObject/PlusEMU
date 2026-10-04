using System.Collections;
using System.Collections.Concurrent;
using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Commands;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users.Clothing;
using Plus.HabboHotel.Users.Effects;
using Plus.HabboHotel.Users.Ignores;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Messenger;
using Plus.HabboHotel.Users.Messenger.FriendBar;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users.Process;
using Plus.Utilities;

using Dapper;
using Plus.HabboHotel.Users.Navigator;

namespace Plus.HabboHotel.Users;

public class Habbo
{
    internal uint WiredRoomNetworkDestination { get; set; }
    public HabboStats HabboStats { get; set; }

    private readonly DateTime _timeCached;

    public GameClient Client { get; set; }
    public ClothingComponent Clothing { get; set; }

    private bool _disconnected;
    private bool _disposed;
    internal bool AccessClosed => WalletClosed || _disposed;
    internal event EventHandler? Disposed;
    public EffectsComponent Effects { get; set; }

    private bool _habboSaved;

    public IgnoresComponent IgnoresComponent { get; set; }
    public InventoryComponent Inventory { get; set; }

    public HabboMessenger Messenger { get; set; }

    public NavigatorPreferences NavigatorPreferences { get; set; }
    public UserAccess Access { get; set; } = UserAccess.Empty;

    [Obsolete("Should be deleted /refactored to standalone service")]
    private ProcessComponent Process { get; set; }

    public ConcurrentDictionary<string, UserAchievement> Achievements = new();
    public ArrayList FavoriteRooms = new();
    public Dictionary<int, int> Quests = new();

    public List<uint> RatedRooms = new();

    // TODO @80O: Convert to uint
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public bool IsAmbassador => Access.Can(PermissionKeys.Ambassador);

    public string Motto { get; set; } = string.Empty;

    public string Look { get; set; } = string.Empty;

    public string Gender { get; set; } = string.Empty;

    internal long LastHabbiconTrigger { get; set; }
    internal object WalletSync { get; } = new();
    internal bool WalletClosed => _habboSaved || _disconnected;

    public int Credits { get; set; }

    public int Duckets { get; set; }

    public int Diamonds { get; set; }

    public int GotwPoints { get; set; }

    public uint HomeRoom { get; set; }

    public DateTimeOffset? LastOnlineAt { get; set; }
    public DateTimeOffset? AccountCreatedAt { get; set; }
    [Obsolete("Convert to Unix time only at the protocol boundary")]
    public double LastOnline => LastOnlineAt?.ToUnixTimeSeconds() ?? 0;
    [Obsolete("Convert to Unix time only at the protocol boundary")]
    public double AccountCreated => AccountCreatedAt?.ToUnixTimeSeconds() ?? 0;

    public List<int> ClientVolume { get; set; } = new() { 0, 0, 0 };

    public DateTimeOffset? LastNameChangedAt { get; set; }
    [Obsolete("Use LastNameChangedAt")]
    public double LastNameChange { get => LastNameChangedAt?.ToUnixTimeSeconds() ?? 0; set => LastNameChangedAt = value <= 0 ? null : DateTimeOffset.FromUnixTimeSeconds((long)value); }

    public string MachineId { get; set; }

    public bool ChatPreference { get; set; }

    public bool FocusPreference { get; set; }

    public bool AllowTradingRequests { get; set; } = true;

    public bool AllowUserFollowing { get; set; }

    public bool AllowMessengerInvites { get; set; }

    public bool AllowPetSpeech { get; set; }

    public bool AllowBotSpeech { get; set; }

    public bool AllowConsoleMessages { get; set; } = true;

    public bool AllowGifts { get; set; }

    public bool AllowMimic { get; set; }

    public bool ReceiveWhispers { get; set; }

    public bool IgnorePublicWhispers { get; set; }

    public FriendBarState FriendbarState { get; set; }

    public int TimeAfk { get; set; }

    public bool DisableForcedEffects { get; set; }

    public bool ChangingName { get; set; }

    public double FloodTime { get; set; }

    public int BannedPhraseCount { get; set; }

    public bool RoomAuthOk { get; set; }

    public int QuestLastCompleted { get; set; }

    public int MessengerSpamCount { get; set; }

    public double MessengerSpamTime { get; set; }

    public double TimeMuted { get; set; }

    public double TradingLockExpiry { get; set; }

    public DateTimeOffset SessionStartedAt { get; internal set; }
    internal IUserPersistenceService Persistence { get; set; }

    public uint TentId { get; set; }

    public uint HopperId { get; set; }

    public bool IsHopping { get; set; }

    public uint TeleporterId { get; set; }

    public bool IsTeleporting { get; set; }

    public uint TeleportingRoomId { get; set; }

    public uint PendingFollowRoomId { get; set; }

    public bool HasSpoken { get; set; }

    public double LastAdvertiseReport { get; set; }

    public bool AdvertisingReported { get; set; }

    public bool AdvertisingReportedBlocked { get; set; }

    public bool WiredInteraction { get; set; }

    public int CustomBubbleId { get; set; }

    public int FastfoodScore { get; set; }

    public int PetId { get; set; }

    public int CreditsUpdateTick { get; set; }

    public ICommandBase ChatCommand { get; set; }

    public DateTime LastGiftPurchaseTime { get; set; }

    public DateTime LastMottoUpdateTime { get; set; }

    public DateTime LastClothingUpdateTime { get; set; }

    public int GiftPurchasingWarnings { get; set; }

    public int MottoUpdateWarnings { get; set; }

    public int ClothingUpdateWarnings { get; set; }

    public bool SessionGiftBlocked { get; set; }

    public bool SessionMottoBlocked { get; set; }

    public bool SessionClothingBlocked { get; set; }

    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(CurrentRoom))]
    public bool InRoom => CurrentRoom != null;

    public Room? CurrentRoom { get; set; }

    internal void Save() { lock (WalletSync) { if (_habboSaved) return; Persistence.Save(this, Access.Can(PermissionKeys.ModerationTickets)); _habboSaved = true; } }

    public bool CacheExpired()
    {
        var span = DateTime.Now - _timeCached;
        return span.TotalMinutes >= 30;
    }

    public bool InitProcess()
    {
        Process = new();
        return Process.Init(this);
    }

    public bool InitFx()
    {
        Effects = new();
        return Effects.Init(this);
    }

    public bool InitClothing()
    {
        Clothing = new();
        return Clothing.Init(this);
    }

    [Obsolete("Each loading task should be moved to their own IUserDataLoadingTask")]
    public void Init(GameClient client)
    {
        // Move each of these loading tasks to their own IUserDataLoadingTask implementation.
        //foreach (var id in data.FavouritedRooms) FavoriteRooms.Add(id);
        Client = client;
        //Quests = data.Quests;
        _disconnected = false;
        InitFx();
        InitClothing();
    }


    public event EventHandler? Disconnected;
    public void OnDisconnect()
    {
        lock (WalletSync) OnDisconnectCore();
    }

    private void OnDisconnectCore()
    {
        if (_disconnected)
            return;

        _disconnected = true;
        Disconnected?.Invoke(this, EventArgs.Empty);

        try
        {
            if (Process != null)
                Process.Dispose();
        }
        catch { }
        // Unregister only after the wallet is saved: until then staff grants see the session and wait on WalletSync,
        // afterwards they write the saved row directly.
        try
        {
            if (!_habboSaved)
            {
                Save();
            }
        }
        finally
        {
            PlusEnvironment.Game.ClientManager.UnregisterClient(Client, Id, Username);
        }
        Dispose();
        Client = null;
    }

    public void Dispose()
    {
        _disposed = true;
        Disposed?.Invoke(this, EventArgs.Empty);
        if (InRoom && CurrentRoom != null)
            CurrentRoom.GetRoomUserManager().RemoveUserFromRoom(Client, false);
        if (Effects != null)
            Effects.Dispose();
        if (Clothing != null)
            Clothing.Dispose();
    }

    public void CheckCreditsTimer()
    {
        lock (WalletSync)
        {
            if (!WalletClosed) CheckCreditsTimerCore();
        }
    }

    private void CheckCreditsTimerCore()
    {
        try
        {
            CreditsUpdateTick--;
            if (CreditsUpdateTick <= 0)
            {
                var creditUpdate = Convert.ToInt32(PlusEnvironment.SettingsManager.TryGetValue("user.currency_scheduler.credit_reward"));
                var ducketUpdate = Convert.ToInt32(PlusEnvironment.SettingsManager.TryGetValue("user.currency_scheduler.ducket_reward"));
                creditUpdate += Access.Limit("limit.currency_credits", 0);
                ducketUpdate += Access.Limit("limit.currency_duckets", 0);
                Credits += creditUpdate;
                Duckets += ducketUpdate;
                Client.Send(new CreditBalanceComposer(Credits));
                Client.Send(new HabboActivityPointNotificationComposer(Duckets, ducketUpdate));
                CreditsUpdateTick = Convert.ToInt32(PlusEnvironment.SettingsManager.TryGetValue("user.currency_scheduler.tick"));
            }
        }
        catch { }
    }


    public int GetQuestProgress(int p)
    {
        Quests.TryGetValue(p, out var progress);
        return progress;
    }

    public UserAchievement? GetAchievementData(string p)
    {
        Achievements.TryGetValue(p, out var achievement);
        return achievement;
    }

    public void ChangeName(string username)
    {
        LastNameChangedAt = DateTimeOffset.UtcNow;
        Username = username;
        SaveKey("username", username);
        Persistence.SetProfileValue(Id, "last_change", LastNameChangedAt.Value.UtcDateTime);
    }

    public void SaveChatBubble(string customBubbleId) => SaveKey("bubble_id", customBubbleId);

    public void SaveKey(string key, string value)
    {
        Persistence.SetProfileValue(Id, key, value);
    }

    public void PrepareRoom(uint id, string password)
    {
        if (Client == null || Client.GetHabbo() == null)
            return;

        if (Client.GetHabbo().InRoom)
        {
            var oldRoom = Client.GetHabbo().CurrentRoom;
            var users = oldRoom?.GetRoomUserManager();
            if (users != null)
                users.RemoveUserFromRoom(Client, false);
            else
            {
                Client.EndCameraContext();
                Client.GetHabbo().CurrentRoom = null;
            }
        }
        if (Client.GetHabbo().IsTeleporting && Client.GetHabbo().TeleportingRoomId != id)
        {
            Client.Send(new CloseConnectionComposer());
            return;
        }
        if (!PlusEnvironment.Game.RoomManager.TryLoadRoom(id, out var room))
        {
            Client.Send(new CloseConnectionComposer());
            return;
        }
        if (room.IsCrashed)
        {
            Client.SendNotification("This room has crashed! :(");
            Client.Send(new CloseConnectionComposer());
            return;
        }
        if (room.GetRoomUserManager().UserCount >= room.UsersMax && !Access.Can(PermissionKeys.RoomEnterFull) && Client.GetHabbo().Id != room.OwnerId)
        {
            Client.Send(new CantConnectComposer(1));
            Client.Send(new CloseConnectionComposer());
            return;
        }
        if (!Access.Can(PermissionKeys.RoomBanOverride) && room.GetBans().IsBanned(Id))
        {
            RoomAuthOk = false;
            Client.GetHabbo().RoomAuthOk = false;
            Client.Send(new CantConnectComposer(4));
            Client.Send(new CloseConnectionComposer());
            return;
        }
        Client.Send(new OpenConnectionComposer());
        if (!room.CheckRights(Client, true, true) && !Client.GetHabbo().IsTeleporting && !Client.GetHabbo().IsHopping)
        {
            if (room.Access == RoomAccess.Doorbell && !Access.Can(PermissionKeys.RoomEnterLocked))
            {
                if (room.UserCount > 0)
                {
                    Client.Send(new DoorbellComposer(""));
                    room.SendPacket(new DoorbellComposer(Client.GetHabbo().Username), true);
                    return;
                }
                Client.Send(new FlatAccessDeniedComposer(""));
                Client.Send(new CloseConnectionComposer());
                return;
            }
            if (room.Access == RoomAccess.Password && !Access.Can(PermissionKeys.RoomEnterLocked))
            {
                if (password.ToLower() != room.Password.ToLower() || string.IsNullOrWhiteSpace(password))
                {
                    Client.Send(new GenericErrorComposer(-100002));
                    Client.Send(new CloseConnectionComposer());
                    return;
                }
            }
        }
        if (!EnterRoom(room))
            Client.Send(new CloseConnectionComposer());
    }

    public bool EnterRoom(Room room)
    {
        if (room == null)
            return false;
        Client.GetHabbo().CurrentRoom = room;
        Client.Send(new RoomReadyComposer(room.RoomId, room.ModelName));
        if (room.Wallpaper != "0.0")
            Client.Send(new RoomPropertyComposer("wallpaper", room.Wallpaper));
        if (room.Floor != "0.0")
            Client.Send(new RoomPropertyComposer("floor", room.Floor));
        Client.Send(new RoomPropertyComposer("landscape", room.Landscape));
        Client.Send(new RoomRatingComposer(room.Score, !(Client.GetHabbo().RatedRooms.Contains(room.RoomId) || room.OwnerId == Client.GetHabbo().Id)));
        using (var dbClient = PlusEnvironment.DatabaseManager.Connection())
        {
            dbClient.Execute("INSERT INTO user_roomvisits (user_id,room_id,entry_timestamp,exit_timestamp) VALUES (@userId, @roomId, @entryTimestamp, @exitTimestamp)",
                new
                {
                    userId = Client.GetHabbo().Id,
                    roomId = Client.GetHabbo().CurrentRoom.RoomId,
                    entryTimestamp = UnixTimestamp.GetNow(),
                    exitTimestamp = 0,
                });
        }

        if (room.OwnerId != Id)
        {
            Client.GetHabbo().HabboStats.RoomVisits += 1;
            PlusEnvironment.Game.AchievementManager.ProgressAchievement(Client, "ACH_RoomEntry", 1);
        }
        return true;
    }
}
