using Plus.Communication.Packets.Outgoing.Inventory.AvatarEffects;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Effects;
using Plus.Utilities;

namespace Plus.Communication.Packets.Incoming.Inventory.AvatarEffects;

internal class AvatarEffectActivatedEvent(IAvatarEffectStore effects) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var effectId = packet.ReadInt();
        var habbo = session.GetHabbo();
        var effect = habbo.Effects.GetEffectNullable(effectId, false, true);
        if (effect == null || habbo.Effects.HasEffect(effectId, true)) return Task.CompletedTask;
        var timestamp = UnixTimestamp.GetNow();
        effects.Activate(effect.Id, timestamp);
        effect.Activated = true;
        effect.TimestampActivated = timestamp;
        session.Send(new AvatarEffectActivatedComposer(effect));
        return Task.CompletedTask;
    }
}
