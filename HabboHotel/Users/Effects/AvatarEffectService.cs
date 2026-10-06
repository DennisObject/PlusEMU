using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Users.Effects;

// Prepared scalars for the outgoing effect packets; composers only write these values.
public sealed record AvatarEffectActivation(int SpriteId, int Duration);

public sealed record AvatarEffectExpiry(int SpriteId);

public sealed record AvatarEffectEntry(int SpriteId, int Duration, int Quantity, bool Activated, int RemainingSeconds);

public interface IAvatarEffectService
{
    // Looks up, checks and activates against one captured instant; the activation persists before the packet is sent.
    void Activate(GameClient session, int effectId);

    // Looks up against one captured instant; a negative id selects nothing, and only an owned, active effect in the room is applied.
    void Select(GameClient session, int effectId);

    // The full effects list at one captured instant, with remaining time measured against that instant.
    ImmutableArray<AvatarEffectEntry> Capture(Habbo habbo);
}

public sealed class AvatarEffectService(TimeProvider time) : IAvatarEffectService
{
    public void Activate(GameClient session, int effectId)
    {
        var habbo = session.GetHabbo();
        var now = time.GetUtcNow();
        var effect = habbo.Effects.GetEffectNullableAt(effectId, now, false, true);

        if (effect == null || habbo.Effects.HasEffectAt(effectId, now, true))
        {
            return;
        }

        effect.Activate(now);
        session.Send(new AvatarEffectActivatedComposer(new AvatarEffectActivation(effect.SpriteId, ToWire(effect.Duration))));
    }

    public void Select(GameClient session, int effectId)
    {
        var selected = effectId < 0 ? 0 : effectId;
        var habbo = session.GetHabbo();

        if (!habbo.InRoom)
        {
            return;
        }

        var room = habbo.CurrentRoom;

        if (room == null)
        {
            return;
        }

        var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);

        if (user == null)
        {
            return;
        }

        var now = time.GetUtcNow();

        if (selected != 0 && habbo.Effects.HasEffectAt(selected, now, true))
        {
            user.ApplyEffect(selected);
        }
    }

    public ImmutableArray<AvatarEffectEntry> Capture(Habbo habbo)
    {
        var now = time.GetUtcNow();

        return habbo.Effects.GetAllEffects
            .Select(effect => new AvatarEffectEntry(effect.SpriteId, ToWire(effect.Duration), effect.Activated ? effect.Quantity - 1 : effect.Quantity,
                effect.Activated, effect.Activated ? ToWire(effect.TimeLeftAt(now)) : -1))
            .ToImmutableArray();
    }

    // Wire fields are 32-bit: values are truncated like the original casts, then held inside [0, int.MaxValue].
    private static int ToWire(double value) => (int)Math.Clamp(value, 0, int.MaxValue);
}
