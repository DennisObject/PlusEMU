using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public class HousekeepingDashboardComposer : IServerPacket
{
    private readonly HousekeepingDashboard _dashboard;
    public uint MessageId => ServerPacketHeader.HousekeepingDashboardComposer;

    public HousekeepingDashboardComposer(HousekeepingDashboard dashboard) => _dashboard = dashboard;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_dashboard.OnlineUsers);
        packet.WriteInteger(_dashboard.TotalUsers);
        packet.WriteInteger(_dashboard.ActiveRooms);
        packet.WriteInteger(_dashboard.TotalRooms);
        packet.WriteInteger(_dashboard.PeakOnlineToday);
        packet.WriteInteger(_dashboard.PeakOnlineAllTime);
        packet.WriteInteger(_dashboard.PendingTickets);
        packet.WriteInteger(_dashboard.SanctionsLast24h);
        packet.WriteInteger(_dashboard.ServerUptimeSeconds);
        packet.WriteString(_dashboard.ServerVersion);
    }
}
