using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

public sealed class ClickUserEvent : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        int virtualId;
        var requestId = 0;

        try {
            virtualId = packet.ReadInt();

            if (packet.HasDataRemaining()) {
                var version = packet.ReadInt();
                var roomId = packet.ReadInt();
                requestId = packet.ReadInt();

                if (version != 1 || roomId != room.Id || requestId <= 0) {
                    return Task.CompletedTask;
                }
            }
        }
        catch (ArgumentException) {
            return Task.CompletedTask;
        }

        if (packet.HasDataRemaining()) {
            return Task.CompletedTask;
        }

        var actor = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        var target = room.GetRoomUserManager().GetRoomUserByVirtualId(virtualId);

        if (actor == null || actor.IsBot || target == null || target.IsBot || target.GetClient()?.GetHabbo() == null) {
            if (requestId != 0) {
                session.Send(new WiredClickUserResponseComposer(virtualId, false, room.Id, requestId, true));
            }

            return Task.CompletedTask;
        }

        var result = room.GetWired().DispatchClickUser(actor, target);

        if (requestId == 0 && result.DoNotRotate) {
            session.Send(new InClientLinkComposer("avatar-info/block-rotate"));
        }

        if (requestId == 0 && result.BlockMenu) {
            session.Send(new InClientLinkComposer("avatar-info/block-menu"));
        }

        session.Send(new WiredClickUserResponseComposer(virtualId, !result.BlockMenu, room.Id, requestId, result.DoNotRotate));

        return Task.CompletedTask;
    }
}
