using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Avatar;

public class SleepComposer : IServerPacket
{
    private readonly int _virtualId;
    private readonly bool _isSleeping;
    public uint MessageId => ServerPacketHeader.SleepComposer;

    public SleepComposer(int virtualId, bool isSleeping)
    {
        _virtualId = virtualId;
        _isSleeping = isSleeping;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_virtualId);
        packet.WriteBoolean(_isSleeping);
    }
}
