using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Effects;

namespace Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;

public class AvatarEffectActivatedComposer(AvatarEffectActivation activation) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.AvatarEffectActivatedComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(activation.SpriteId);
        packet.WriteInteger(activation.Duration);
        packet.WriteBoolean(false); //Permanent
    }
}
