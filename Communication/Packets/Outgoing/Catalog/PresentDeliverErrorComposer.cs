using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class PresentDeliverErrorComposer : IServerPacket
{
    private readonly bool _creditError;
    private readonly bool _ducketError;

    public uint MessageId => ServerPacketHeader.PresentDeliverErrorComposer;

    public PresentDeliverErrorComposer(bool creditError, bool ducketError)
    {
        _creditError = creditError;
        _ducketError = ducketError;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(_creditError);
        packet.WriteBoolean(_ducketError);
    }
}
