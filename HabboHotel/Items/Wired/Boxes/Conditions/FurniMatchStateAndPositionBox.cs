using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Boxes.Conditions;

internal class FurniMatchStateAndPositionBox : IWiredItem
{
    public FurniMatchStateAndPositionBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();
    }

    public Room Instance { get; set; }

    public Item Item { get; set; }

    public WiredBoxType Type => WiredBoxType.ConditionMatchStateAndPosition;

    public ConcurrentDictionary<uint, Item> SetItems { get; set; }

    public string StringData { get; set; } = string.Empty;

    public bool BoolData { get; set; }

    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet)
    {
        if (SetItems.Count > 0)
            SetItems.Clear();
        var unknown = packet.ReadInt();
        var state = packet.ReadInt();
        var direction = packet.ReadInt();
        var placement = packet.ReadInt();
        var unknown2 = packet.ReadString();
        var furniCount = packet.ReadInt();
        for (var i = 0; i < furniCount; i++)
        {
            var selectedItem = Instance.GetRoomItemHandler().GetItem(packet.ReadUInt());
            if (selectedItem != null)
                SetItems.TryAdd(selectedItem.Id, selectedItem);
        }
        StringData = $"{state};{direction};{placement}";
    }

    public bool Execute(params object[] @params)
    {
        if (@params.Length == 0)
            return false;
        if (string.IsNullOrEmpty(StringData) || StringData == "0;0;0" || SetItems.Count == 0)
            return false;
        foreach (var item in SetItems.Values.ToList())
        {
            if (item == null)
                continue;
            if (!Instance.GetRoomItemHandler().GetFloor.Contains(item))
                continue;
            foreach (var I in ItemsData.Split(';'))
            {
                if (string.IsNullOrEmpty(I))
                    continue;
                var ii = Instance.GetRoomItemHandler().GetItem(Convert.ToUInt32(I.Split(':')[0]));
                if (ii == null)
                    continue;
                var partsString = I.Split(':');
                var part = partsString[1].Split(',');
                if (int.Parse(StringData.Split(';')[0]) == 1) //State
                {
                    try
                    {
                        if (ii.LegacyDataString != part[4])
                            return false;
                    }
                    catch { }
                }
                if (int.Parse(StringData.Split(';')[1]) == 1) //Direction
                {
                    try
                    {
                        if (ii.Rotation != Convert.ToInt32(part[3]))
                            return false;
                    }
                    catch { }
                }
                if (int.Parse(StringData.Split(';')[2]) == 1) //Position
                {
                    try
                    {
                        if (ii.GetX != Convert.ToInt32(part[0]) || ii.GetY != Convert.ToInt32(part[1]) ||
                            ii.GetZ != Convert.ToDouble(part[2]))
                            return false;
                    }
                    catch { }
                }
            }
        }
        return true;
    }
}