using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

public sealed class UnseenResetItemsEvent(IHabbiconService service) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        int category = packet.ReadInt(), count = packet.ReadInt();
        if (category != HabbiconService.UnseenCategory || count <= 0 || count > 1000) return Task.CompletedTask;
        // Never turn a truncated or empty item reset into a reset of the entire category.
        if (packet.Buffer.Length < count * 4) return Task.CompletedTask;
        var ids = new HashSet<int>();
        for (int i = 0; i < count; i++) ids.Add(packet.ReadInt());
        service.ClearUnseen(session.GetHabbo().Id, ids.ToArray());
        return Task.CompletedTask;
    }
}
