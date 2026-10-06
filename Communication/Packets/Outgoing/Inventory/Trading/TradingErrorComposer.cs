using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Trading;

public class TradingErrorComposer : IServerPacket
{
    private readonly TradingError _error;
    private readonly string _username;
    public uint MessageId => ServerPacketHeader.TradingErrorComposer;

    public TradingErrorComposer(TradingError error, string username)
    {
        _error = error;
        _username = username;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger((int)_error);
        packet.WriteString(_username);
    }
}
