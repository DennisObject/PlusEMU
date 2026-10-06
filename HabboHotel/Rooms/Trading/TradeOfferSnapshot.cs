using System.Collections.Immutable;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Rooms.Trading;

public sealed record TradeItemWireData(uint Id, string Type, int SpriteId, uint UniqueNumber, uint UniqueSeries, bool IsFloor);
public sealed record TradeOfferSnapshot(int UserId, ImmutableArray<TradeItemWireData> Items, int ExchangeCredits)
{
    public static ImmutableArray<TradeOfferSnapshot> Capture(Trade trade) => trade.Users.Select(user =>
    {
        var items = user.OfferedItems.Values.ToArray();
        var data = items.Select(item => new TradeItemWireData(item.Id, item.Definition.Type.ToString().ToLowerInvariant(),
            item.Definition.SpriteId, item.UniqueNumber, item.UniqueSeries, item.Definition.Type == ItemType.Floor)).ToImmutableArray();
        var credits = items.Where(item => item.Definition.InteractionType == InteractionType.Exchange).Sum(item => item.Definition.BehaviourData);
        return new TradeOfferSnapshot(user.RoomUser.UserId, data, credits);
    }).ToImmutableArray();
}
