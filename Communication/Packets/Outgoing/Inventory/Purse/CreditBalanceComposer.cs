using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Purse;

public class CreditBalanceComposer : IServerPacket
{
    private readonly int _creditsBalance;
    public uint MessageId => ServerPacketHeader.CreditBalanceComposer;

    public CreditBalanceComposer(int creditsBalance)
    {
        _creditsBalance = creditsBalance;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteString($"{_creditsBalance}.0");
}
