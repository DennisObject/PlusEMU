using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Addons;

/// <summary>Modern boxes publish immutable configurations through the transactional save adapter.</summary>
public abstract class WiredConfiguredBehaviorBox : IWiredConfiguredItem
{
    protected WiredConfiguredBehaviorBox(Room room, Item item, WiredBoxDescriptor descriptor,
        Func<WiredConfiguration, WiredConfiguration> normalize)
    {
        Instance = room;
        Item = item;
        Descriptor = descriptor with { Support = WiredBoxSupport.Implemented };
        _normalize = normalize;
        Configuration = normalize(new());
    }

    private readonly Func<WiredConfiguration, WiredConfiguration> _normalize;
    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxDescriptor Descriptor { get; }
    public WiredBoxType Type => WiredBoxType.None;
    public WiredConfiguration Configuration { get; private set; }
    public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
    public string StringData { get; set; } = "";
    public string ItemsData { get; set; } = "";
    public bool BoolData { get; set; }

    public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        try
        {
            validated = _normalize(proposed);
            error = "";
            return true;
        }
        catch (ArgumentException exception)
        {
            validated = Configuration;
            error = exception.Message;
            return false;
        }
    }

    public void ApplyConfiguration(WiredConfiguration validated)
    {
        Configuration = validated;
        StringData = validated.Text;
        ItemsData = string.Join(';', validated.SelectedItems);
        ConfigurationChanged();
    }

    protected virtual void ConfigurationChanged() { }
    public void HandleSave(IIncomingPacket packet) => throw new InvalidOperationException("Modern boxes require the configured save adapter");
    public bool Execute(params object[] @params) => false;
}
