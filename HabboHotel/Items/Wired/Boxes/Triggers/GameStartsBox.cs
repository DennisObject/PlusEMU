using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Boxes.Triggers;

internal class GameStartsBox : IWiredItem
{
    public GameStartsBox(Room instance, Item item)
    {
        Item = item;
        Instance = instance;
        SetItems = new();
    }

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.TriggerGameStarts;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; } = string.Empty;
    public bool BoolData { get; set; }
    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet) { }

    public bool Execute(params object[] @params)
    {
        var accepted = false;
        foreach (var user in Instance.GetRoomUserManager().GetRoomUsers().ToList())
        {
            var player = user?.GetClient()?.GetHabbo();
            if (player != null)
                accepted |= Instance.GetWired().RunStack(this, player);
        }
        return accepted;
    }
}
