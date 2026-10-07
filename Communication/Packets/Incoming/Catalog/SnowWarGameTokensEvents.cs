using Plus.Communication.Packets.Incoming.Game;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal sealed class GetSnowWarGameTokensOfferEvent(ISnowStormDirectory directory) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!packet.HasDataRemaining()) {
            directory.ShowTokenOffers(session);
        }

        return Task.CompletedTask;
    }
}

internal sealed class PurchaseSnowWarGameTokensOfferEvent(ISnowStormDirectory directory) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (SnowStormPacketReader.TryReadInts(packet, 1, out var values)) {
            directory.PurchaseTokens(session, values[0]);
        }

        return Task.CompletedTask;
    }
}
