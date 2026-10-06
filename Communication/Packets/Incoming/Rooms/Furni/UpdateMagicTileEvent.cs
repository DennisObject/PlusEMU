using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal class UpdateMagicTileEvent(IMagicTileService tiles) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadUInt();
        var requestedHeight = packet.ReadInt();
        bool? multiWalk = packet.HasDataRemaining() ? packet.ReadBool() : null;
        tiles.Update(session, itemId, requestedHeight, multiWalk);

        return Task.CompletedTask;
    }
}
