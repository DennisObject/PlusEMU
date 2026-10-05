using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;

public class AvatarEffectActivatedComposer : IServerPacket
{
    private readonly Plus.HabboHotel.Users.Effects.AvatarEffect _effect;
    public uint MessageId => ServerPacketHeader.AvatarEffectActivatedComposer;

    public AvatarEffectActivatedComposer(Plus.HabboHotel.Users.Effects.AvatarEffect effect)
    {
        _effect = effect;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_effect.SpriteId);
        packet.WriteInteger((int)_effect.Duration);
        packet.WriteBoolean(false); //Permanent
    }
}