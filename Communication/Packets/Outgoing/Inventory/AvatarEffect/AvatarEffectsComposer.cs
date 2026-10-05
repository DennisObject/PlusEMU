using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;

public class AvatarEffectsComposer : IServerPacket
{
    private readonly ICollection<Plus.HabboHotel.Users.Effects.AvatarEffect> _effects;

    public uint MessageId => ServerPacketHeader.AvatarEffectsComposer;

    public AvatarEffectsComposer(ICollection<Plus.HabboHotel.Users.Effects.AvatarEffect> effects)
    {
        _effects = effects;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_effects.Count);
        foreach (var effect in _effects)
        {
            packet.WriteInteger(effect.SpriteId); //Effect Id
            packet.WriteInteger(0); //Type, 0 = Hand, 1 = Full
            packet.WriteInteger((int)effect.Duration);
            packet.WriteInteger(effect.Activated ? effect.Quantity - 1 : effect.Quantity);
            packet.WriteInteger(effect.Activated ? (int)effect.TimeLeft : -1);
            packet.WriteBoolean(false); //Permanent
        }

    }
}