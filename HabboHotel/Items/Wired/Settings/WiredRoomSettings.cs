using System.Runtime.CompilerServices;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Settings;

/// <summary>One room-scoped snapshot; successful storage precedes every live settings publication.</summary>
public sealed class WiredRoomSettings(Room room, IWiredRoomSettingsStore store, Func<TimeZoneInfo>? hotelTimeZone = null)
{
    private static readonly ConditionalWeakTable<Room, WiredRoomSettings> Instances = new();
    private readonly object _gate = new();
    private bool _loaded;
    private WiredRoomSettingsSnapshot? _saved;

    public static WiredRoomSettings For(Room room, IDatabase? database = null) =>
        Instances.GetValue(room, key => new(key, new DatabaseWiredRoomSettingsStore(database ?? PlusEnvironment.DatabaseManager)));

    public WiredRoomSettingsSnapshot Snapshot { get { lock (_gate) { EnsureLoaded(); return _saved ?? new(); } } }
    public TimeZoneInfo? ExplicitTimeZone => Snapshot.TimeZoneId is { Length: > 0 } timezone
        ? TimeZoneInfo.FindSystemTimeZoneById(timezone) : null;
    public TimeZoneInfo TimeZone
    {
        get
        {
            return ExplicitTimeZone ?? hotelTimeZone?.Invoke() ?? TimeZoneInfo.Local;
        }
    }

    public bool CanManage(GameClient session) => InRoom(session) && room.CheckRights(session, true);
    public bool CanInspect(GameClient session) => Permitted(session, false);
    public bool CanModify(GameClient session) => Permitted(session, true);
    public WiredRoomSettingsView View(GameClient session)
    {
        lock (_gate) return new(Snapshot, CanInspect(session), CanModify(session), CanManage(session));
    }

    public bool TrySave(GameClient session, int inspect, int modify, string? timezone, out string error)
    {
        error = "Unable to save Wired room settings.";
        if (!CanManage(session)) { error = "You do not have permission to manage Wired room settings."; return false; }
        if (!WiredRoomSettingsSnapshot.TryValidate(inspect, modify, timezone, out var validated))
        { error = "Invalid Wired permissions or timezone."; return false; }
        lock (_gate)
        {
            EnsureLoaded();
            // Check the session again immediately before the authorized transaction.
            if (!CanManage(session)) return false;
            store.Save(room.Id, session.GetHabbo().Id, session.GetHabbo().Permissions.HasRight("room_any_owner"), _saved, validated);
            _saved = validated;
            error = "";
            return true;
        }
    }

    /// <summary>Reconcile an explicit client reload with storage under the same gate as configuration saves.</summary>
    public void Reload()
    {
        lock (_gate)
        {
            var saved = store.Load(room.Id);
            // Publish only a successful authoritative read; a deleted row restores the existing Plus fallback.
            _saved = saved;
            _loaded = true;
        }
    }

    private bool Permitted(GameClient session, bool modify)
    {
        if (!InRoom(session)) return false;
        if (CanManage(session)) return true;
        lock (_gate)
        {
            EnsureLoaded();
            // No companion row preserves Plus's existing room and group decorator permissions.
            if (_saved == null) return room.CheckRights(session, false, true);
            var mask = (WiredRoomAccess)(modify ? _saved.ModifyMask : _saved.InspectMask);
            var habbo = session.GetHabbo();
            if (!modify && mask.HasFlag(WiredRoomAccess.Everyone)) return true;
            if (room.Type == "private" && habbo.Permissions.HasRight("room_any_rights")) return true;
            if (mask.HasFlag(WiredRoomAccess.Rights) && room.UsersWithRights.Contains(habbo.Id)) return true;
            if (mask.HasFlag(WiredRoomAccess.GroupMembers) && room.Group?.IsMember(habbo.Id) == true) return true;
            return mask.HasFlag(WiredRoomAccess.GroupAdmins) && room.Group?.IsAdmin(habbo.Id) == true;
        }
    }

    private bool InRoom(GameClient session) => session?.GetHabbo() is { } habbo && ReferenceEquals(habbo.CurrentRoom, room);
    private void EnsureLoaded()
    {
        if (!_loaded) Reload();
    }
}
