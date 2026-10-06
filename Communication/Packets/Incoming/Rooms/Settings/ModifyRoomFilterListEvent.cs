using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Settings;

internal sealed class ModifyRoomFilterListEvent(IRoomFilterService filters) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var roomId = packet.ReadInt();
        var added = packet.ReadBool();
        var word = packet.ReadString();
        filters.Modify(session, roomId, added, word);

        return Task.CompletedTask;
    }
}
