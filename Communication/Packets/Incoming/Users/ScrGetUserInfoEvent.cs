using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Users;

internal class ScrGetUserInfoEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (packet.ReadString() == "habbo_club") session.Send(new ScrSendUserInfoComposer(session.GetHabbo().Access));
        return Task.CompletedTask;
    }
}
