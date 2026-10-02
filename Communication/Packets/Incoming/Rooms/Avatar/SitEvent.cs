using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Runtime;

namespace Plus.Communication.Packets.Incoming.Rooms.Avatar;

internal class SitEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().InRoom)
            return Task.CompletedTask;
        var posture = packet.ReadInt();
        if (posture is not (0 or 1))
            return Task.CompletedTask;
        var room = session.GetHabbo().CurrentRoom;
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (user == null)
            return Task.CompletedTask;
        if (user.Statusses.ContainsKey("lie") || user.IsLying || user.RidingHorse || user.IsWalking) return Task.CompletedTask;
        var wasSitting = user.IsSitting;
        if (posture == 1 && !user.Statusses.ContainsKey("sit"))
        {
            if (user.RotBody % 2 == 0)
            {
                try
                {
                    user.Statusses.Add("sit", "1.0");
                    user.Z -= 0.35;
                    user.IsSitting = true;
                    user.UpdateNeeded = true;
                }
                catch
                {
                    //ignored
                }
            }
            else
            {
                user.RotBody--;
                user.Statusses.Add("sit", "1.0");
                user.Z -= 0.35;
                user.IsSitting = true;
                user.UpdateNeeded = true;
            }
        }
        else if (posture == 0 && user.IsSitting)
        {
            user.Z += 0.35;
            user.Statusses.Remove("sit");
            user.Statusses.Remove("1.0");
            user.IsSitting = false;
            user.UpdateNeeded = true;
        }
        if (wasSitting != user.IsSitting)
            room.GetWired().Dispatch(new(WiredEventKind.AvatarAction) { Actor = user,
                Action = (int)(user.IsSitting ? WiredAvatarAction.Sit : WiredAvatarAction.Stand), Code = -1 });
        return Task.CompletedTask;
    }
}