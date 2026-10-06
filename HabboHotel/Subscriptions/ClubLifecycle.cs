using Dapper;
using Microsoft.Extensions.Logging;
using Plus.Core;
using Plus.Core.FigureData;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Styles;
using Plus.HabboHotel.Users;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Avatar;

namespace Plus.HabboHotel.Subscriptions;

public sealed class ClubLifecycle(IAccessControl permissions, IClubRewards rewards, IGameClientManager clients,
    IFigureDataManager figures, IChatStyleManager styles, IRoomManager rooms, ISettingsManager settings,
    IDatabase database, TimeProvider clock, ILogger<ClubLifecycle> logger) : IStartable, IDisposable
{
    private ITimer? _timer;
    private int _running;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _announcedGifts = new();
    public int StartOrder => 80;

    public Task Start()
    {
        permissions.AccessChanged += Normalize;
        _timer = clock.CreateTimer(_ => Tick(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        return Task.CompletedTask;
    }

    private readonly Dictionary<int, Habbo> _sessions = new();
    private readonly object _sessionSync = new();
    private void Track(Habbo habbo)
    {
        lock (_sessionSync)
        {
            if (_sessions.TryGetValue(habbo.Id, out var previous))
            {
                if (ReferenceEquals(previous, habbo)) return;
                previous.Disconnected -= Disconnected;
                previous.Disposed -= Disconnected;
            }
            _sessions[habbo.Id] = habbo;
            habbo.Disconnected += Disconnected;
            habbo.Disposed += Disconnected;
        }
    }
    private void Disconnected(object? sender, EventArgs args)
    {
        if (sender is not Habbo habbo) return;
        lock (_sessionSync)
        {
            habbo.Disconnected -= Disconnected;
            habbo.Disposed -= Disconnected;
            if (_sessions.TryGetValue(habbo.Id, out var current) && ReferenceEquals(current, habbo))
            { _sessions.Remove(habbo.Id); _announcedGifts.TryRemove(habbo.Id, out _); }
        }
    }

    public void Normalize(Habbo habbo)
    {
        Track(habbo);
        var level = ClubAccess.LevelFor(habbo.Access);
        var look = figures.ProcessFigure(habbo.Look, habbo.Gender, habbo.Clothing.GetClothingParts, level);
        if (look != habbo.Look)
        {
            habbo.Look = look;
            using var connection = database.Connection();
            connection.Execute("UPDATE users SET look = @look WHERE id = @id", new { id = habbo.Id, look });
            habbo.Client.Send(new AvatarAspectUpdateComposer(look, habbo.Gender));
            if (habbo.CurrentRoom?.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id) is { } user)
            {
                habbo.Client.Send(new UserChangeComposer(AvatarChangeSnapshot.Capture(user, true)));
                habbo.CurrentRoom.SendPacket(new UserChangeComposer(AvatarChangeSnapshot.Capture(user, false)));
            }
        }
        if (habbo.CustomBubbleId != 0 && (!styles.TryGetStyle(habbo.CustomBubbleId, out var style) || !style.CanUse(habbo.Access)))
        { habbo.CustomBubbleId = 0; habbo.SaveChatBubble("0"); }
        habbo.Client.Send(new ScrSendUserInfoComposer(habbo.Access, habbo.Access.Membership.ExpiresAt is not null && !habbo.Access.Membership.Active(clock.GetUtcNow()) ? ScrSendUserInfoComposer.ExpiringResponse : ScrSendUserInfoComposer.InfoResponse));
        habbo.Client.Send(new MessengerInitComposer(ClubLimits.For(habbo.Access, "friends", settings)));
        var limit = ClubLimits.For(habbo.Access, "visitors", settings);
        using (var connection = database.Connection())
            connection.Execute("UPDATE rooms SET users_max = LEAST(users_max, @limit), allow_hidewall = IF(@club, allow_hidewall, 0), wallthick = IF(@club, wallthick, 0), floorthick = IF(@club, floorthick, 0) WHERE owner = @id",
                new { id = habbo.Id, limit, club = level > 0 });
        foreach (var room in rooms.GetRooms().Where(room => room.OwnerId == habbo.Id))
        {
            room.UsersMax = Math.Min(room.UsersMax, limit);
            if (level == 0) { room.Hidewall = false; room.WallThickness = 0; room.FloorThickness = 0; }
            room.SendPacket(new RoomVisualizationSettingsComposer(room.WallThickness, room.FloorThickness, room.Hidewall));
        }
        if (level == 0 && habbo.CurrentRoom?.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id) is { DanceId: > 1 } dancer)
        { dancer.DanceId = 0; habbo.CurrentRoom.SendPacket(new DanceComposer(dancer.VirtualId, 0)); }
        AnnounceGifts(habbo);
    }

    private void AnnounceGifts(Habbo habbo)
    {
        var now = clock.GetUtcNow();
        var count = habbo.Access.Membership.Active(now) ? habbo.Access.Membership.AvailableGifts(now) : 0;
        if (!_announcedGifts.TryGetValue(habbo.Id, out var previous) || count != previous)
        { _announcedGifts[habbo.Id] = count; if (count > 0) habbo.Client.Send(new PickMonthlyClubGiftComposer(count)); }
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) return;
        try
        {
            rewards.RunPaydays();
            var online = clients.GetClients.ToArray().Select(client => client.GetHabbo()).Where(habbo => habbo != null).ToArray();
            foreach (var habbo in online) AnnounceGifts(habbo!);
            var ids = online.Select(habbo => habbo!.Id).ToHashSet();
            foreach (var id in _announcedGifts.Keys) if (!ids.Contains(id)) _announcedGifts.TryRemove(id, out _);
        }
        catch (Exception exception) { logger.LogError(exception, "Habbo Club lifecycle failed"); }
        finally { Volatile.Write(ref _running, 0); }
    }
    public void Dispose()
    {
        permissions.AccessChanged -= Normalize; _timer?.Dispose();
        lock (_sessionSync)
        {
            foreach (var habbo in _sessions.Values)
            { habbo.Disconnected -= Disconnected; habbo.Disposed -= Disconnected; }
            _sessions.Clear(); _announcedGifts.Clear();
        }
    }
}
