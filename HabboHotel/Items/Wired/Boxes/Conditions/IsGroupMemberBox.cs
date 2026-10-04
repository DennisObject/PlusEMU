using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Wired.Boxes.Conditions;

internal class IsGroupMemberBox : IWiredItem
{
    public IsGroupMemberBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();
    }

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.ConditionIsGroupMember;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; } = string.Empty;
    public bool BoolData { get; set; }
    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet)
    {
        var unknown = packet.ReadInt();
        var unknown2 = packet.ReadString();
    }

    public bool Execute(params object[] @params)
    {
        if (@params.Length == 0)
            return false;
        var player = (Habbo)@params[0];
        if (player == null)
            return false;
        if (Instance.Group == null)
            return false;
        if (!Instance.Group.IsMember(player.Id))
            return false;
        return true;
    }
}