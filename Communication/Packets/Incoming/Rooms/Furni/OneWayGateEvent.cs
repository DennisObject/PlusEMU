using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal class OneWayGateEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return Task.CompletedTask;
        var item = room.GetRoomItemHandler().GetItem(packet.ReadUInt());
        if (item == null || item.IsTemporary)
            return Task.CompletedTask;
        var hasRights = room.CheckRights(session);
        if (item.Definition.InteractionType == InteractionType.OneWayGate)
        {
            item.Interactor.OnTrigger(session, item, -1, hasRights);
            return Task.CompletedTask;
        }
        return Task.CompletedTask;
    }
}