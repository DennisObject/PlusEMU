using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

public sealed class UnseenResetCategoryEvent(IHabbiconService service) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (packet.ReadInt() == HabbiconService.UnseenCategory) service.ClearUnseen(session.GetHabbo().Id, Array.Empty<int>());
        return Task.CompletedTask;
    }
}
