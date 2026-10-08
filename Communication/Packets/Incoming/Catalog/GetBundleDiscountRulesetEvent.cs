using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal sealed class GetBundleDiscountRulesetEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.Send(new CatalogItemDiscountComposer());

        return Task.CompletedTask;
    }
}
