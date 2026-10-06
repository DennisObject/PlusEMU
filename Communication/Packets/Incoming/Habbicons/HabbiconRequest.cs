using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

// Concrete handlers retain PacketManager's convention of one type per header.
public abstract class HabbiconRequest(IHabbiconPresentationService presentation) : IPacketEvent
{
    protected virtual HabbiconAction? Action => null;
    protected virtual bool Info => false;
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var id = Action != null || Info ? packet.ReadInt() : 0;

        if (Action is { } action)
        {
            presentation.Change(session, action, id);
        }
        else if (Info)
        {
            presentation.ShowInfo(session, id);
        }
        else
        {
            presentation.ShowShop(session);
        }

        return Task.CompletedTask;
    }
}
