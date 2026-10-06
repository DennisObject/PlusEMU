using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Configured boxes execute through the contextual engine and retain the editor's wire representation.</summary>
public abstract class WiredModernBox : IWiredContextualItem
{
    private WiredConfiguration _configuration = new();
    protected WiredModernBox(Room room, Item item, WiredBoxDescriptor descriptor)
    {
        Instance = room;
        Item = item;
        Descriptor = descriptor with
        {
            Support = WiredBoxSupport.Implemented
        };
    }
    public Room Instance
    {
        get; set;
    }
    public Item Item
    {
        get; set;
    }
    public WiredBoxType Type => WiredBoxType.None;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
    public string StringData { get; set; } = "";
    public bool BoolData
    {
        get; set;
    }
    public string ItemsData { get; set; } = "";
    public WiredBoxDescriptor Descriptor
    {
        get;
    }
    public WiredConfiguration Configuration => Volatile.Read(ref _configuration);
    public abstract bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error);
    public void ApplyConfiguration(WiredConfiguration validated) => Interlocked.Exchange(ref _configuration, validated);
    public void HandleSave(IIncomingPacket packet) => throw new InvalidOperationException("Configured Wired uses WiredConfigurationSave.");
    public bool Execute(params object[] arguments) => arguments is [WiredRuntimeContext context] && Execute(context);
    public abstract bool Execute(WiredRuntimeContext context);
    protected Item[] Furni(WiredRuntimeContext context, WiredConfiguration config, string slot, bool secondary = false) =>
        context.Targets.ResolveFurni(context, secondary ? config.SecondarySelectedItems : config.SelectedItems,
            config.FurniSources.GetValueOrDefault(slot));
    protected RoomUser[] Users(WiredRuntimeContext context, WiredConfiguration config, string slot, string? name = null) =>
        context.Targets.ResolveUsers(context, [], config.UserSources.GetValueOrDefault(slot), name);
    protected static int Param(WiredConfiguration config, int index, int fallback = 0) =>
        config.IntParams.Length > index ? config.IntParams[index] : fallback;
}
