using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Navigator;

internal class EditRoomPromotionEvent(IRoomPromotionService promotions) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var roomId = packet.ReadUInt();
        var name = packet.ReadString();
        var description = packet.ReadString();
        promotions.Edit(session, new(roomId, name, description));

        return Task.CompletedTask;
    }
}
