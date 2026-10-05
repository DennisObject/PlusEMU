using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal sealed class ManageGroupEvent(IGroupManagementSnapshotService service) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        service.Send(session, packet.ReadInt());
        return Task.CompletedTask;
    }
}
