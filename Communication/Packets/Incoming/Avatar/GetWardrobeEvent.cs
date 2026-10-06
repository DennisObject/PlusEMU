using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Clothing;

namespace Plus.Communication.Packets.Incoming.Avatar;

internal class GetWardrobeEvent(IAvatarWardrobeService wardrobe) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => wardrobe.ShowWardrobe(session);
}
