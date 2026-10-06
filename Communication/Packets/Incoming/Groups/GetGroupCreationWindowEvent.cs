using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal sealed class GetGroupCreationWindowEvent(IGroupPresentationService presentation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        presentation.ShowCreationWindow(session);

        return Task.CompletedTask;
    }
}
