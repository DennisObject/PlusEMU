using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Boxes.Effects;

internal class RegenerateMapsBox : IWiredItem
{
    public RegenerateMapsBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        StringData = "";
        SetItems = new();
    }

    public Room Instance { get; set; }
    public Item Item { get; set; }

    public WiredBoxType Type => WiredBoxType.EffectRegenerateMaps;

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
        if (Instance == null)
            return false;
        var timeSinceRegen = DateTime.Now - Instance.LastRegeneration;
        if (timeSinceRegen.TotalMinutes > 1)
        {
            Instance.GetGameMap().GenerateMaps();
            Instance.LastRegeneration = DateTime.Now;
            return true;
        }
        return false;
    }
}