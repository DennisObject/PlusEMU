using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal class UpdateMagicTileAdjacentEvent(IMagicTileService tiles) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadUInt();
        var moveDown = packet.ReadBool();
        tiles.UpdateAdjacent(session, itemId, moveDown);

        return Task.CompletedTask;
    }
}
