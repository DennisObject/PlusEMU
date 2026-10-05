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
    // Persists the activation before the in-memory effect and the packet change.
    void Activate(GameClient session, int effectId);

    // A negative id selects nothing; only an owned, active effect in the room is applied.
    void Select(GameClient session, int effectId);

    // The full effects list at the captured instant, with remaining time measured against the injected clock.
    ImmutableArray<AvatarEffectEntry> Capture(Habbo habbo);
}

public sealed class AvatarEffectService(IAvatarEffectStore store, TimeProvider time) : IAvatarEffectService
{
    public void Activate(GameClient session, int effectId)
    {
        var habbo = session.GetHabbo();
        var effect = habbo.Effects.GetEffectNullable(effectId, false, true);
        if (effect == null || habbo.Effects.HasEffect(effectId, true)) return;
        var now = time.GetUtcNow();
        store.Activate(effect.Id, now);
        effect.Activated = true;
        effect.ActivatedAt = now;
        session.Send(new AvatarEffectActivatedComposer(new AvatarEffectActivation(effect.SpriteId, (int)effect.Duration)));
    }

    public void Select(GameClient session, int effectId)
    {
        var selected = effectId < 0 ? 0 : effectId;
        var habbo = session.GetHabbo();
        if (!habbo.InRoom) return;
        var room = habbo.CurrentRoom;
        if (room == null) return;
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
        if (user == null) return;
        if (selected != 0 && habbo.Effects.HasEffect(selected, true))
            user.ApplyEffect(selected);
    }

    public ImmutableArray<AvatarEffectEntry> Capture(Habbo habbo)
    {
        var now = time.GetUtcNow();
        return habbo.Effects.GetAllEffects
            .Select(effect => new AvatarEffectEntry(effect.SpriteId, (int)effect.Duration, effect.Activated ? effect.Quantity - 1 : effect.Quantity,
                effect.Activated, effect.Activated ? (int)RemainingSeconds(effect, now) : -1))
            .ToImmutableArray();
    }

    // Same rule as AvatarEffect.TimeLeft, measured against the captured instant and never negative.
    private static double RemainingSeconds(AvatarEffect effect, DateTimeOffset now)
    {
        var used = effect.ActivatedAt is { } activatedAt ? (now - activatedAt).TotalSeconds : 0;
        return Math.Max(0, effect.Duration - used);
    }
}
