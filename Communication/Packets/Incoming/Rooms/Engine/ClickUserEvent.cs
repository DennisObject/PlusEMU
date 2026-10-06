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

        try
        {
            virtualId = packet.ReadInt();
        }
        catch (ArgumentException) { return Task.CompletedTask; }

        if (packet.HasDataRemaining())
        {
            return Task.CompletedTask;
        }

        var actor = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        var target = room.GetRoomUserManager().GetRoomUserByVirtualId(virtualId);

        if (actor == null || actor.IsBot || target == null || target.IsBot || target.GetClient()?.GetHabbo() == null)
        {
            return Task.CompletedTask;
        }

        var result = room.GetWired().DispatchClickUser(actor, target);

        if (result.DoNotRotate)
        {
            session.Send(new InClientLinkComposer("avatar-info/block-rotate"));
        }

        if (result.BlockMenu)
        {
            session.Send(new InClientLinkComposer("avatar-info/block-menu"));
        }

        session.Send(new WiredClickUserResponseComposer(virtualId, !result.BlockMenu));

        return Task.CompletedTask;
    }
}
