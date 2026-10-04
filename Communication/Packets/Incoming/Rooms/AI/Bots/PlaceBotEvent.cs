using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Communication.Packets.Incoming.Rooms.AI.Bots;

internal sealed class PlaceBotEvent(IBotManagementService bots) : RoomPacketEvent
{
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var botId = packet.ReadInt();
        var x = packet.ReadInt();
        var y = packet.ReadInt();
        bots.Place(room, session, botId, x, y);
        return Task.CompletedTask;
    }
}
