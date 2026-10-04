using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Boxes.Effects;

internal class SetRollerSpeedBox : IWiredItem
{
    public SetRollerSpeedBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();
        if (SetItems.Count > 0)
            SetItems.Clear();
    }

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.EffectSetRollerSpeed;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; } = string.Empty;
    public bool BoolData { get; set; }
    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet)
    {
        if (SetItems.Count > 0)
            SetItems.Clear();
        var unknown = packet.ReadInt();
        var message = packet.ReadString();
        StringData = message;
        if (!int.TryParse(StringData, out var speed)) StringData = "";
    }

    public bool Execute(params object[] @params)
    {
        if (int.TryParse(StringData, out var speed)) Instance.GetRoomItemHandler().SetSpeed(speed);
        return true;
    }
}