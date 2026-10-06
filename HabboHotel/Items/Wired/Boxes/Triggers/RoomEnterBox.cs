using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Wired.Boxes.Triggers;

internal class RoomEnterBox : IWiredItem
{
    public RoomEnterBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();
    }

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.TriggerRoomEnter;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; } = string.Empty;
    public bool BoolData { get; set; }
    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet)
    {
        var unknown = packet.ReadInt();
        var user = packet.ReadString();
        StringData = user;
    }

    public bool Execute(params object[] @params)
    {
        var player = (Habbo)@params[0];
        if (!string.IsNullOrWhiteSpace(StringData) && player.Username != StringData)
            return false;
        return Instance.GetWired().RunStack(this, player);
    }
}
