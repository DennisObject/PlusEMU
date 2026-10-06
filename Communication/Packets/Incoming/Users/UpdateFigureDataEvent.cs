using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Users;

internal class UpdateFigureDataEvent(IUserProfileService profiles) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        profiles.UpdateFigure(session, new(packet.ReadString(), packet.ReadString()));

        return Task.CompletedTask;
    }
}
