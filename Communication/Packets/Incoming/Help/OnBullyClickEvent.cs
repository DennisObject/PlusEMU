using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Help;

internal class OnBullyClickEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        //I am a very boring packet.
        return Task.CompletedTask;
    }
}
