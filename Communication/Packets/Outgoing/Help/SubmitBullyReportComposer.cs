using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Help;

public class SubmitBullyReportComposer : IServerPacket
{
    private readonly BullyReportResult _result;
    public uint MessageId => ServerPacketHeader.SubmitBullyReportComposer;

    public SubmitBullyReportComposer(BullyReportResult result)
    {
        _result = result;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger((int)_result);
}