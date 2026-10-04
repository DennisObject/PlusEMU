using Plus.Communication.Packets.Outgoing.Inventory.AvatarEffects;
using Plus.Utilities;
using Dapper;

namespace Plus.HabboHotel.Users.Effects;

public sealed class AvatarEffect
{
    public AvatarEffect(int id, int userId, int spriteId, double duration, bool activated, double timestampActivated, int quantity, IAvatarEffectStore? store = null)
    {
        Id = id;
        UserId = userId;
        SpriteId = spriteId;
        Duration = duration;
        Activated = activated;
        TimestampActivated = timestampActivated;
        Quantity = quantity;
        _store = store;
    }

    private readonly IAvatarEffectStore? _store;

    public int Id { get; set; }

    public int UserId { get; set; }

    public int SpriteId { get; set; }

    public double Duration { get; set; }

    public bool Activated { get; set; }

    public double TimestampActivated { get; set; }

    public int Quantity { get; set; }

    public double TimeUsed => UnixTimestamp.GetNow() - TimestampActivated;

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
        var tsNow = UnixTimestamp.GetNow();
        Store.Activate(Id, tsNow);
        Activated = true;
        TimestampActivated = tsNow;
        return true;
    }

    public void HandleExpiration(Habbo habbo)
    {
        Quantity--;
        Activated = false;
        TimestampActivated = 0;
        Store.SaveQuantity(Id, Quantity, false, 0);
        habbo.Client.Send(new AvatarEffectExpiredComposer(this));
        // reset fx if in room?
    }

    public void AddToQuantity()
    {
        Quantity++;
        Store.SaveQuantity(Id, Quantity, Activated, TimestampActivated);
    }

    private IAvatarEffectStore Store => _store ?? new AvatarEffectStore(PlusEnvironment.DatabaseManager);
}
