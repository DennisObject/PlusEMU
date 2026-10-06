using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Boxes;

internal class AddonRandomEffectBox : IWiredItem
{
    public AddonRandomEffectBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();

        if (SetItems.Count > 0) {
            SetItems.Clear();
        }
    }

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.AddonRandomEffect;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; } = string.Empty;
    public bool BoolData { get; set; }
    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet) { }

    public bool Execute(params object[] @params) => true;
}
