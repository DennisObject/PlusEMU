using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Help;

internal class GetSanctionStatusEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        //Session.SendMessage(new SanctionStatusComposer());
        return Task.CompletedTask;
    }
}
