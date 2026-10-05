using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;

public class AvatarEffectExpiredComposer : IServerPacket
{
    private readonly Plus.HabboHotel.Users.Effects.AvatarEffect _effect;
    public uint MessageId => ServerPacketHeader.AvatarEffectExpiredComposer;

    public AvatarEffectExpiredComposer(Plus.HabboHotel.Users.Effects.AvatarEffect effect)
    {
        _effect = effect;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(_effect.SpriteId);
}