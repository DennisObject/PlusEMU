using System.Collections.Concurrent;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Core;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Boxes.Effects;

internal class MatchPositionBox : IWiredItem, IWiredCycle
{
    private int _delay;

    public MatchPositionBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();
        TickCount = Delay;
    }

    public int Delay
    {
        get => _delay;
        set
        {
            _delay = value;
            TickCount = value + 1;
        }
    }

    public int TickCount { get; set; }

    // Scheduling belongs to the room engine; this contract is retained for saved delays.
    public bool OnCycle() => false;

    public Room Instance { get; set; }

    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.EffectMatchPosition;

    public ConcurrentDictionary<uint, Item> SetItems { get; set; }

    public string StringData { get; set; }

    public bool BoolData { get; set; }
    public string ItemsData { get; set; }

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
        var delay = packet.ReadInt();
        Delay = delay;
    }

    public bool Execute(params object[] @params)
    {
        if (string.IsNullOrEmpty(StringData) || StringData == "0;0;0" || SetItems.Count == 0)
            return false;
        foreach (var item in SetItems.Values.ToList())
        {
            if (Instance.GetRoomItemHandler().GetFloor == null || !Instance.GetRoomItemHandler().GetFloor.Contains(item))
                continue;
            foreach (var I in ItemsData.Split(';'))
            {
                if (string.IsNullOrEmpty(I))
                    continue;
                var itemId = Convert.ToInt32(I.Split(':')[0]);
                if (itemId != item.Id)
                    continue;
                var ii = Instance.GetRoomItemHandler().GetItem(Convert.ToUInt32(itemId));
                if (ii == null)
                    continue;
                var partsString = I.Split(':');
                try
                {
                    if (string.IsNullOrEmpty(partsString[0]) || string.IsNullOrEmpty(partsString[1]))
                        continue;
                }
                catch
                {
                    continue;
                }
                var part = partsString[1].Split(',');
                try
                {
                    if (int.Parse(StringData.Split(';')[0]) == 1) //State
                    {
                        if (part.Length >= 5)
                            SetState(ii, part[4]);
                        else
                            SetState(ii, "1");
                    }
                }
                catch (Exception e)
                {
                    ExceptionLogger.LogWiredException(e);
                }
                try
                {
                    if (int.Parse(StringData.Split(';')[1]) == 1) //Direction
                        SetRotation(ii, Convert.ToInt32(part[3]));
                }
                catch (Exception e)
                {
                    ExceptionLogger.LogWiredException(e);
                }
                try
                {
                    if (int.Parse(StringData.Split(';')[2]) == 1) //Position
                        SetPosition(ii, Convert.ToInt32(part[0]), Convert.ToInt32(part[1]), Convert.ToDouble(part[2]));
                }
                catch (Exception e)
                {
                    ExceptionLogger.LogWiredException(e);
                }
            }
        }
        return true;
    }

    private void SetState(Item item, string extradata)
    {
        if (item.LegacyDataString == extradata)
            return;
        if (item.Definition.InteractionType == InteractionType.Dice)
            return;
        item.LegacyDataString = extradata;
        item.UpdateState(false, true);
    }

    private void SetRotation(Item item, int rotation)
    {
        if (item.Rotation == rotation)
            return;
        if (Instance.GetRoomItemHandler().SetFloorItem(null!, item, item.GetX, item.GetY, rotation, false, false, false, height: item.GetZ))
            item.UpdateState(false, true);
    }

    private void SetPosition(Item item, int coordX, int coordY, double coordZ)
    {
        Instance.SendPacket(new SlideObjectBundleComposer(item.GetX, item.GetY, item.GetZ, coordX, coordY, coordZ, 0, 0, item.Id));
        Instance.GetRoomItemHandler().SetFloorItem(item, coordX, coordY, coordZ);
        //Instance.GetGameMap().GenerateMaps();
    }
}