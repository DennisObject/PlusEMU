using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Trading;

public class TradingStartComposer : IServerPacket
{
    private readonly int _user1Id;
    private readonly int _user2Id;
    public uint MessageId => ServerPacketHeader.TradingStartComposer;

    public TradingStartComposer(int user1Id, int user2Id)
    {
        _user1Id = user1Id;
        _user2Id = user2Id;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_user1Id);
        packet.WriteInteger(1);
        packet.WriteInteger(_user2Id);
        packet.WriteInteger(1);
    }
}
