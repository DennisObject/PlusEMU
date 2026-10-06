using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class GetGroupInfoEvent(IGroupPresentationService presentation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        var newWindow = packet.ReadBool();
        presentation.ShowInfo(session, groupId, newWindow);
        return Task.CompletedTask;
    }
}
