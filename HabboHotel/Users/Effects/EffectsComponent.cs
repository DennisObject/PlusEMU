using System.Collections.Concurrent;
using Plus.Communication.Packets.Outgoing.Rooms.Avatar;

namespace Plus.HabboHotel.Users.Effects;

public sealed class EffectsComponent
{
    /// <summary>
    /// Effects stored by ID > Effect.
    /// </summary>
    private readonly ConcurrentDictionary<int, AvatarEffect> _effects = new();
    private readonly TimeProvider _time;
    private Habbo _habbo;

    public EffectsComponent(TimeProvider time)
    {
        _time = time;
    }

    internal EffectsComponent(IEnumerable<AvatarEffect> effects, Habbo habbo, TimeProvider time)
    {
        _time = time;
        foreach (var effect in effects) _effects.TryAdd(effect.Id, effect);
        _habbo = habbo;
    }

    public ICollection<AvatarEffect> GetAllEffects => _effects.Values;

    public int CurrentEffect { get; set; }

    /// <summary>
    /// Initializes the EffectsComponent.
    /// </summary>
    public bool Init(Habbo habbo)
    {
        if (_effects.Count > 0)
            return false;
        _habbo = habbo;
        CurrentEffect = 0;
        return true;
    }


    public bool TryAdd(AvatarEffect effect) => _effects.TryAdd(effect.Id, effect);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="spriteId"></param>
    /// <param name="activatedOnly"></param>
    /// <param name="unactivatedOnly"></param>
    /// <returns></returns>
    public bool HasEffect(int spriteId, bool activatedOnly = false, bool unactivatedOnly = false) =>
        HasEffectAt(spriteId, _time.GetUtcNow(), activatedOnly, unactivatedOnly);

    public bool HasEffectAt(int spriteId, DateTimeOffset now, bool activatedOnly = false, bool unactivatedOnly = false) =>
        GetEffectNullableAt(spriteId, now, activatedOnly, unactivatedOnly) != null;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="spriteId"></param>
    /// <param name="activatedOnly"></param>
    /// <param name="unactivatedOnly"></param>
    /// <returns></returns>
    public AvatarEffect? GetEffectNullable(int spriteId, bool activatedOnly = false, bool unactivatedOnly = false) =>
        GetEffectNullableAt(spriteId, _time.GetUtcNow(), activatedOnly, unactivatedOnly);

    public AvatarEffect? GetEffectNullableAt(int spriteId, DateTimeOffset now, bool activatedOnly = false, bool unactivatedOnly = false)
    {
        foreach (var effect in _effects.Values.ToList())
            if (!effect.HasExpiredAt(now) && effect.SpriteId == spriteId && (!activatedOnly || effect.Activated) && (!unactivatedOnly || !effect.Activated))
                return effect;
        return null;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="habbo"></param>
    public void CheckEffectExpiry(Habbo habbo)
    {
        var now = _time.GetUtcNow();
        foreach (var effect in _effects.Values.ToList())
            if (effect.HasExpiredAt(now))
                effect.HandleExpiration(habbo);
    }

    public void ApplyEffect(int effectId)
    {
        if (_habbo == null || _habbo.CurrentRoom == null)
            return;
        var user = _habbo.CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(_habbo.Id);
        if (user == null)
            return;
        CurrentEffect = effectId;
        if (user.IsDancing)
            _habbo.CurrentRoom.SendPacket(new DanceComposer(user.VirtualId, 0));
        _habbo.CurrentRoom.SendPacket(new AvatarEffectComposer(user.VirtualId, effectId));
    }

    /// <summary>
    /// Disposes the EffectsComponent.
    /// </summary>
    public void Dispose()
    {
        _effects.Clear();
    }
}
