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
            TickCount = TicksToWait;
        }
    }

    public int TickCount
    {
        get; set;
    }

    // The room ticks every half second and fires the repeater on the tick that finds it at zero, so a delay of
    // N half-seconds waits N - 1 ticks in between.
    private int TicksToWait => Math.Max(0, _delay - 1);

    public bool OnCycle()
    {
        // The timer always resets, including a blocked stack; a failed condition does not
        // turn this into a trigger that polls on every room cycle.
        TickCount = TicksToWait;
        var actors = Instance.GetRoomUserManager().GetRoomUsers()
            .Select(user => user?.GetClient()?.GetHabbo())
            .OfType<Plus.HabboHotel.Users.Habbo>()
            .Select(player => new object[] { player })
            .ToArray();

        return Instance.GetWired().RunPeriodicStack(this, actors);
    }

    public Room Instance
    {
        get; set;
    }
    public Item Item
    {
        get; set;
    }
    public WiredBoxType Type => WiredBoxType.TriggerRepeat;
    public ConcurrentDictionary<uint, Item> SetItems
    {
        get; set;
    }
    public string StringData { get; set; } = string.Empty;
    public bool BoolData
    {
        get; set;
    }
    public string ItemsData { get; set; } = string.Empty;

    public void HandleSave(IIncomingPacket packet)
    {
        var unknown = packet.ReadInt();
        var delay = packet.ReadInt();
        Delay = delay;
    }

    public bool Execute(params object[] @params) => true;
}
