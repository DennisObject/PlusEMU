using Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;

namespace Plus.HabboHotel.Users.Effects;

public sealed class AvatarEffect
{
    public AvatarEffect(int id, int userId, int spriteId, double duration, bool activated, DateTimeOffset? timestampActivated, int quantity, IAvatarEffectStore? store = null)
    {
        Id = id;
        UserId = userId;
        SpriteId = spriteId;
        Duration = duration;
        Activated = activated;
        ActivatedAt = timestampActivated;
        Quantity = quantity;
        _store = store;
    }

    private readonly IAvatarEffectStore? _store;

    public int Id { get; set; }

    public int UserId { get; set; }

    public int SpriteId { get; set; }

    public double Duration { get; set; }

    public bool Activated { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public int Quantity { get; set; }

    // Remaining time is measured against an instant the caller captured, never the wall clock.
    public double TimeLeftAt(DateTimeOffset now)
    {
        // A timestamp in the future or missing counts as no time used yet, so the remaining time never exceeds the duration.
        var used = ActivatedAt is { } activatedAt ? Math.Max(0, (now - activatedAt).TotalSeconds) : 0;
        var remaining = Activated ? Duration - used : Duration;

        return Math.Max(0, remaining);
    }

    public bool HasExpiredAt(DateTimeOffset now) => Activated && TimeLeftAt(now) <= 0;

    /// <summary>
    /// Persists the activation at the captured UTC instant, then changes the model.
    /// </summary>
    public void Activate(DateTimeOffset utcNow)
    {
        Store.Activate(Id, utcNow);
        Activated = true;
        ActivatedAt = utcNow;
    }

    public void HandleExpiration(Habbo habbo)
    {
        // The lower quantity is persisted before the model or the packet changes.
        var quantity = Quantity - 1;
        Store.SaveQuantity(Id, quantity, false, null);
        Quantity = quantity;
        Activated = false;
        ActivatedAt = null;
        habbo.Client?.Send(new AvatarEffectExpiredComposer(new AvatarEffectExpiry(SpriteId)));
        // reset fx if in room?
    }

    public void AddToQuantity()
    {
        var quantity = Quantity + 1;
        Store.SaveQuantity(Id, quantity, Activated, ActivatedAt);
        Quantity = quantity;
    }

    private IAvatarEffectStore Store => _store ?? throw new InvalidOperationException("Avatar effect persistence is not configured.");
}
