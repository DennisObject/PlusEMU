using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class PurchaseOKComposer : IServerPacket
{
    private readonly CatalogPurchaseConfirmation? _confirmation;
    public uint MessageId => ServerPacketHeader.PurchaseOKComposer;

    public PurchaseOKComposer(CatalogPurchaseConfirmation? confirmation = null)
    {
        _confirmation = confirmation;
    }

    public void Compose(IOutgoingPacket packet)
    {
        if (_confirmation != null) {

            packet.WriteUInteger(_confirmation.Id);
            packet.WriteString(_confirmation.Name);
            packet.WriteBoolean(false);
            packet.WriteInteger(_confirmation.Credits);
            packet.WriteInteger(_confirmation.Points);
            packet.WriteInteger(0);
            packet.WriteBoolean(true);
            packet.WriteInteger(1);
            packet.WriteString(_confirmation.ProductType);

            if (_confirmation.ProductType == "b") {
                packet.WriteString(_confirmation.Name);
                packet.WriteInteger(0);
                packet.WriteBoolean(false);
            }
            else {
                packet.WriteInteger(_confirmation.SpriteId);
                packet.WriteString("");
                packet.WriteInteger(1);
                packet.WriteInteger(0);
                packet.WriteString("");
                packet.WriteInteger(1);
            }
        }
        else {
            packet.WriteInteger(0);
            packet.WriteString("");
            packet.WriteBoolean(false);
            packet.WriteInteger(0);
            packet.WriteInteger(0);
            packet.WriteInteger(0);
            packet.WriteBoolean(true);
            packet.WriteInteger(1);
            packet.WriteString("s");
            packet.WriteInteger(0);
            packet.WriteString("");
            packet.WriteInteger(1);
            packet.WriteInteger(0);
            packet.WriteString("");
            packet.WriteInteger(1);
        }
    }
}
