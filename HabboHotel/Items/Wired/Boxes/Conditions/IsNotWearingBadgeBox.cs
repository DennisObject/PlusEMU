using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Wired.Boxes.Conditions;

internal class IsNotWearingBadgeBox : IWiredItem
{
    public IsNotWearingBadgeBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();
    }

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.ConditionIsNotWearingBadge;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; } = string.Empty;
    public bool BoolData { get; set; }
    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet)
    {
        var unknown = packet.ReadInt();
        var badgeCode = packet.ReadString();
        StringData = badgeCode;
    }

    public bool Execute(params object[] @params)
    {
        if (@params.Length == 0) {
            return false;
        }

        if (string.IsNullOrEmpty(StringData)) {
            return false;
        }

        var player = (Habbo)@params[0];

        if (player == null) {
            return false;
        }

        if (player.Inventory is not { } inventory || !inventory.Badges.HasBadge(StringData)) {
            return true;
        }

        if (!inventory.Badges.EquippedBadges.Any()) {
            return true;
        }

        return inventory.Badges.EquippedBadges.All(badge => !badge.Code.Equals(StringData));
    }
}
