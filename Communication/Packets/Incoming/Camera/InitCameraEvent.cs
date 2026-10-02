using Plus.Communication.Packets.Outgoing.Camera;
using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Camera;

public sealed class InitCameraEvent(ICameraCheckoutService checkout) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.IsAuthenticated || packet.Buffer.Length != 0) return Task.CompletedTask;
        var credits = 0;
        var points = 0;
        var publish = 0;
        try
        {
            if (checkout.Enabled)
            {
                var prices = checkout.Prices;
                credits = prices.Credits;
                points = prices.Points;
                publish = prices.PublishPoints;
            }
        }
        catch (Exception)
        {
            credits = 0;
            points = 0;
            publish = 0;
        }

        session.Send(new InitCameraComposer(credits, points, publish));
        return Task.CompletedTask;
    }
}
