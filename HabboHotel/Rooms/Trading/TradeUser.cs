using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Rooms.Trading;

public sealed class TradeUser
{
    public TradeUser(RoomUser user)
    {
        RoomUser = user;
        HasAccepted = false;
        OfferedItems = new();
    }

    public RoomUser RoomUser
    {
        get;
    }

    public bool HasAccepted
    {
        get; set;
    }

    public Dictionary<uint, InventoryItem> OfferedItems
    {
        get; set;
    }
}
