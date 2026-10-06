using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Clothing;

namespace Plus.Communication.Packets.Incoming.Avatar;

internal class SaveWardrobeOutfitEvent(IAvatarWardrobeService wardrobe) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var slotId = packet.ReadInt();
        var look = packet.ReadString();
        var gender = packet.ReadString();
        wardrobe.SaveOutfit(session.GetHabbo(), slotId, look, gender);

        return Task.CompletedTask;
    }
}
