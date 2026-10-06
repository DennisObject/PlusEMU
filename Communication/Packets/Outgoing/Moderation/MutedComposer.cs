using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public class MutedComposer : IServerPacket
{
    private readonly double _timeMuted;

    public uint MessageId => ServerPacketHeader.MutedComposer;

    public MutedComposer(double timeMuted)
    {
        _timeMuted = timeMuted;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(Convert.ToInt32(_timeMuted));
}
