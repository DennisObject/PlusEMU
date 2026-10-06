using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Boxes.Effects;

internal class GiveRewardBox : IWiredItem
{
    public GiveRewardBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();
        if (SetItems.Count > 0)
            SetItems.Clear();
    }

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.EffectGiveReward;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; } = string.Empty;
    public bool BoolData { get; set; }
    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet)
    {
        if (SetItems.Count > 0)
            SetItems.Clear();
        var unknown = packet.ReadInt();
        var time = packet.ReadInt();
        var message = packet.ReadString();

        //this.StringData = Time + ";" + Message;
    }

    public bool Execute(params object[] @params) => true;
}