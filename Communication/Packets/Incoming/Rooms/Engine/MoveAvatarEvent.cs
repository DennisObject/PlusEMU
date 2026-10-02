using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Runtime;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

internal class MoveAvatarEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().InRoom)
            return Task.CompletedTask;
        var room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return Task.CompletedTask;
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (user == null)
            return Task.CompletedTask;
        var moveX = packet.ReadInt();
        var moveY = packet.ReadInt();
        if (!room.GetGameMap().ValidTile(moveX, moveY))
            return Task.CompletedTask;
        if (!user.IsBot)
            room.GetWired().Dispatch(new(WiredEventKind.ClickTile) { Actor = user, X = moveX, Y = moveY });
        if (!user.CanWalk || moveX == user.X && moveY == user.Y)
            return Task.CompletedTask;
        if (user.RidingHorse)
        {
            var horse = room.GetRoomUserManager().GetRoomUserByVirtualId(user.HorseId);
            if (horse != null)
                horse.MoveTo(moveX, moveY);
        }
        user.MoveTo(moveX, moveY);
        return Task.CompletedTask;
    }
}