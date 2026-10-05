using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

public sealed class UnseenResetCategoryEvent(IHabbiconPresentationService habbicons) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var category = packet.ReadInt();
        habbicons.ResetUnseenCategory(session, category);
        return Task.CompletedTask;
    }
}
