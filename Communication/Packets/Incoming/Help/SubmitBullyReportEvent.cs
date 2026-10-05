using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Incoming.Help;

internal sealed class SubmitBullyReportEvent(IAdvertisingReportService reports) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        reports.Submit(session, packet.ReadInt());
        return Task.CompletedTask;
    }
}
