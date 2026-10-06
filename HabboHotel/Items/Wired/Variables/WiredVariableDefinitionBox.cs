using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Passive variable metadata. Reads and mutations run through the room module, never as a stack action.</summary>
public sealed class WiredVariableDefinitionBox : IWiredConfiguredItem, IWiredEditorConfigurationProvider, IWiredConfigurationPersistenceProvider
{
    private readonly WiredVariableConfigurationPersistence? _persistence;
    private readonly WiredVariableEditor? _editor;
    public WiredVariableDefinitionBox(Room room, Item item, WiredBoxDescriptor descriptor,
        WiredVariableConfigurationPersistence? persistence = null, WiredVariableEditor? editor = null)
    {
        if (!WiredVariableDefinitions.Supports(descriptor.CanonicalName))
        {
            throw new ArgumentException("Unsupported variable definition.", nameof(descriptor));
        }

        Instance = room;
        Item = item;
        Descriptor = descriptor with
        {
            Support = WiredBoxSupport.Implemented
        };
        _persistence = persistence;
        _editor = editor;
        Configuration = WiredVariableDefaults.Create(descriptor.CanonicalName);
    }
    public Room Instance
    {
        get; set;
    }
    public Item Item
    {
        get; set;
    }
    public WiredBoxType Type => Item.Definition.WiredType;
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
    public WiredConfiguration Configuration { get; private set; } = new();
    public bool HasPersistedConfiguration
    {
        get; private set;
    }
    public WiredConfiguration GetEditorConfiguration() => _editor?.ForDisplay(Descriptor.CanonicalName, Item.Id, Configuration) ?? Configuration;
    public void PersistConfiguration(WiredConfiguration validated) =>
        (_persistence ?? throw new InvalidOperationException("Definition persistence is not bound.")).Persist(this, validated);
    public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        validated = proposed;

        return WiredVariableDefinitions.TryDecode(Descriptor.CanonicalName, Item.Id, Instance.Id,
            Instance.OwnerId > 0 ? (uint)Instance.OwnerId : 0, proposed, out _, out error);
    }
    public void ApplyConfiguration(WiredConfiguration validated)
    {
        Configuration = validated;
        HasPersistedConfiguration = true;
        StringData = validated.Text;
        ItemsData = string.Join(';', validated.SelectedItems);
    }
    public bool Execute(params object[] arguments) => false;
    public void HandleSave(IIncomingPacket packet) => throw new InvalidOperationException("Variable definitions require validated configuration persistence.");
}
