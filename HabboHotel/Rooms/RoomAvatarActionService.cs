using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Runtime;

namespace Plus.HabboHotel.Rooms;

public interface IRoomAvatarActionService
{
    void ApplySign(Room room, GameClient session, int signId);
}

public sealed class RoomAvatarActionService(TimeProvider clock) : IRoomAvatarActionService
{
    private static readonly TimeSpan SignDuration = TimeSpan.FromSeconds(5);

    public void ApplySign(Room room, GameClient session, int signId)
    {
        if (signId is < 0 or > 17)
            return;
        var habbo = session.GetHabbo();
        if (habbo.CurrentRoom != room)
            return;
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
        if (user == null)
            return;
        var now = clock.GetUtcNow();
        user.UnIdle();
        user.SetStatus("sign", Convert.ToString(signId));
        user.UpdateNeeded = true;
        user.SignExpiresAt = now + SignDuration;
        room.GetWired().Dispatch(new(WiredEventKind.AvatarAction)
            { Actor = user, Action = (int)WiredAvatarAction.Sign, Code = signId });
    }
}
