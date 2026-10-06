using Plus.Communication.Packets.Outgoing.Habbicons;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Quests;

namespace Plus.HabboHotel.Habbicons;

public interface IRoomHabbiconService
{
    void Trigger(GameClient session, int id);
}

public sealed class RoomHabbiconService(IHabbiconService habbicons, IRewardTrackManager rewards,
    TimeProvider clock) : IRoomHabbiconService
{
    public void Trigger(GameClient session, int id)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;
        var user = room?.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
        if (room == null || user == null || id <= 0 || id > 1000000)
            return;
        var now = clock.GetUtcNow().ToUniversalTime();
        if (habbo.LastHabbiconTriggeredAt is { } last && now - last < TimeSpan.FromSeconds(1))
            return;
        if ((habbo.FloodUntil is { } floodUntil && now < floodUntil) || habbo.TimeMuted > 0
            || (!habbo.Access.Can(PermissionKeys.RoomIgnoreMute) && room.CheckMute(session, now)))
            return;
        if (!habbo.Access.Can(PermissionKeys.ModerationTool) && user.IncrementAndCheckFlood(now, out var muteTime))
        {
            session.Send(new FloodControlComposer(muteTime));
            return;
        }
        if (!habbicons.Use(habbo.Id, id))
            return;
        rewards.Progress(session, RewardTrackActions.UseHabbicon);
        habbo.LastHabbiconTriggeredAt = now;
        user.UnIdle();
        room.SendPacket(new RoomUseHabbiconComposer(user.VirtualId, id));
        session.Send(new UserHabbiconsComposer(habbicons.Load(habbo.Id)));
    }
}
