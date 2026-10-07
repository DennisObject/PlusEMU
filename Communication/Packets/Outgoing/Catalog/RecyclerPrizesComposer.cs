using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Recycler;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public sealed class RecyclerPrizesComposer(ImmutableArray<RecyclerPrizeList> levels) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.RecyclerPrizesComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(levels.Length);
        foreach (var level in levels) {
            packet.WriteInteger(level.Level);
            packet.WriteInteger(level.Chance);
            packet.WriteInteger(level.Products.Length);
            foreach (var product in level.Products) {
                packet.WriteString(product.Code);
                packet.WriteInteger(1);
                packet.WriteString(product.Type);
                packet.WriteInteger(product.SpriteId);
            }
        }
    }
}

public sealed class RecyclerStatusComposer(int status, int secondsToWait) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.RecyclerStatusComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(status);
        packet.WriteInteger(secondsToWait);
    }
}

public sealed class RecyclerFinishedComposer(int status, int prizeId) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.RecyclerFinishedComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(status);
        packet.WriteInteger(prizeId);
    }
}
