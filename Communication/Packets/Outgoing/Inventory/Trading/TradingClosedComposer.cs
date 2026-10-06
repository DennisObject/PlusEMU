using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Trading;

public class TradingClosedComposer : IServerPacket
{
    private readonly int _userId;
    public uint MessageId => ServerPacketHeader.TradingClosedComposer;

    public TradingClosedComposer(int userId)
    {
        _userId = userId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_userId);
        packet.WriteInteger(0);
    }
}
