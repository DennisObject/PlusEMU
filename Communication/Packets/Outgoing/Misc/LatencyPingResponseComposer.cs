using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Misc;

public class LatencyPingResponseComposer : IServerPacket
{
    private readonly int _testResponse;
    public uint MessageId => ServerPacketHeader.LatencyPingResponseComposer;

    public LatencyPingResponseComposer(int testResponse)
    {
        _testResponse = testResponse;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(_testResponse);
}
