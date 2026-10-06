using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Advertisement;

internal class GetInterstitialMessageEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => throw new NotImplementedException();
}
