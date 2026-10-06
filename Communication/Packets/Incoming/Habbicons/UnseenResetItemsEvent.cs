using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

public sealed class UnseenResetItemsEvent(IHabbiconPresentationService habbicons) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        int category = packet.ReadInt(), count = packet.ReadInt();

        if (count <= 0 || count > 1000)
        {
            return Task.CompletedTask;
        }

        // Never turn a truncated or empty item reset into a reset of the entire category.
        if (packet.Buffer.Length < count * 4)
        {
            return Task.CompletedTask;
        }

        var ids = new int[count];

        for (int i = 0; i < count; i++)
        {
            ids[i] = packet.ReadInt();
        }

        habbicons.ResetUnseenItems(session, category, ids);

        return Task.CompletedTask;
    }
}
