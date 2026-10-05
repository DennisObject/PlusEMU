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

    public double TimeUsed => ActivatedAt is { } activatedAt ? (DateTimeOffset.UtcNow - activatedAt).TotalSeconds : 0;

    public double TimeLeft
    {
        get
        {
            var tl = Activated ? Duration - TimeUsed : Duration;
            if (tl < 0) tl = 0;
            return tl;
        }
    }

    public bool HasExpired => Activated && TimeLeft <= 0;

    /// <summary>
    /// Activates the AvatarEffect
    /// </summary>
    public bool Activate()
    {
        var tsNow = DateTimeOffset.UtcNow;
        Store.Activate(Id, tsNow);
        Activated = true;
        ActivatedAt = tsNow;
        return true;
    }

    public void HandleExpiration(Habbo habbo)
    {
        Quantity--;
        Activated = false;
        ActivatedAt = null;
        Store.SaveQuantity(Id, Quantity, false, null);
        habbo.Client.Send(new AvatarEffectExpiredComposer(this));
        // reset fx if in room?
    }

    public void AddToQuantity()
    {
        Quantity++;
        Store.SaveQuantity(Id, Quantity, Activated, ActivatedAt);
    }

    private IAvatarEffectStore Store => _store ?? throw new InvalidOperationException("Avatar effect persistence is not configured.");
}
