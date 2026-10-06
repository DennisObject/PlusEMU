using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Effects;

namespace Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;

public class AvatarEffectsComposer(ImmutableArray<AvatarEffectEntry> effects) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.AvatarEffectsComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(effects.Length);

        foreach (var effect in effects) {
            packet.WriteInteger(effect.SpriteId); //Effect Id
            packet.WriteInteger(0); //Type, 0 = Hand, 1 = Full
            packet.WriteInteger(effect.Duration);
            packet.WriteInteger(effect.Quantity);
            packet.WriteInteger(effect.RemainingSeconds);
            packet.WriteBoolean(false); //Permanent
        }
    }
}
