using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Effects;

namespace Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;

public class AvatarEffectExpiredComposer(AvatarEffectExpiry expiry) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.AvatarEffectExpiredComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(expiry.SpriteId);
}
