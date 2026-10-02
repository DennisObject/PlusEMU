using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Boxes.Triggers;

internal class RepeaterBox : IWiredItem, IWiredCycle
{
    private int _delay;

    public RepeaterBox(Room instance, Item item)
    {
        Instance = instance;
        Item = item;
        SetItems = new();
    }

    public int Delay
    {
        get => _delay;
        set
        {
            _delay = value;
            TickCount = value;
        }
    }

    public int TickCount { get; set; }

    public bool OnCycle()
    {
        // The timer always resets, including a blocked stack; a failed condition does not
        // turn this into a trigger that polls on every room cycle.
        TickCount = Delay;
        var actors = Instance.GetRoomUserManager().GetRoomUsers()
            .Select(user => user?.GetClient()?.GetHabbo())
            .Where(player => player != null)
            .Select(player => new object[] { player })
            .ToArray();
        return Instance.GetWired().RunPeriodicStack(this, actors);
    }

    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => WiredBoxType.TriggerRepeat;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; }
    public string StringData { get; set; }
    public bool BoolData { get; set; }
    public string ItemsData { get; set; }

    public void HandleSave(IIncomingPacket packet)
    {
        var unknown = packet.ReadInt();
        var delay = packet.ReadInt();
        Delay = delay;
        TickCount = delay;
    }

    public bool Execute(params object[] @params) => true;
}