using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Trading;

namespace Plus.Communication.Packets.Outgoing.Inventory.Trading;

public sealed class TradingUpdateComposer(ImmutableArray<TradeOfferSnapshot> users) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.TradingUpdateComposer;

    public void Compose(IOutgoingPacket packet)
    {
        foreach (var user in users)
        {
            packet.WriteInteger(user.UserId);
            packet.WriteInteger(user.Items.Length);
            foreach (var item in user.Items)
            {
                packet.WriteUInteger(item.Id);
                packet.WriteString(item.Type);
                packet.WriteUInteger(item.Id);
                packet.WriteInteger(item.SpriteId);
                packet.WriteInteger(0);
                if (item.UniqueNumber > 0)
                {
                    packet.WriteBoolean(false);
                    packet.WriteInteger(256);
                    packet.WriteString(string.Empty);
                    packet.WriteUInteger(item.UniqueNumber);
                    packet.WriteUInteger(item.UniqueSeries);
                }
                else
                {
                    packet.WriteBoolean(true);
                    packet.WriteInteger(0);
                    packet.WriteString(string.Empty);
                }
                packet.WriteInteger(0);
                packet.WriteInteger(0);
                packet.WriteInteger(0);
                if (item.IsFloor)
                    packet.WriteInteger(0);
            }
            packet.WriteInteger(user.Items.Length);
            packet.WriteInteger(user.ExchangeCredits);
        }
    }
}
