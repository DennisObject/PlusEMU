using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Concrete scalar action/condition adapter; refresh live holders while retaining the firing's context values.</summary>
public sealed class WiredVariableConfiguredBox : IWiredContextualItem
{
    private readonly WiredVariableExecutors _executors;
    public WiredVariableConfiguredBox(Room room, Item item, WiredBoxDescriptor descriptor, WiredVariableExecutors executors)
    {
        if (!WiredVariableExecutors.Supports(descriptor.CanonicalName)) throw new ArgumentException("No scalar executor for this box.", nameof(descriptor));
        Instance = room; Item = item; _executors = executors;
        Descriptor = descriptor with { Support = WiredBoxSupport.Implemented };
    }
    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxType Type => Item.Definition.WiredType;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
    public string StringData { get; set; } = "";
    public bool BoolData { get; set; }
    public string ItemsData { get; set; } = "";
    public WiredBoxDescriptor Descriptor { get; }
    public WiredConfiguration Configuration { get; private set; } = new();
    public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        validated = proposed;
        return WiredVariableExecutors.TryValidate(Descriptor.CanonicalName, proposed, out error);
    }
    public void ApplyConfiguration(WiredConfiguration validated)
    {
        Configuration = validated;
        StringData = validated.Text;
        ItemsData = string.Join(';', validated.SelectedItems);
    }
    public bool Execute(params object[] arguments) => arguments.OfType<WiredVariableFrame>().FirstOrDefault() is { } frame
        && _executors.Execute(Descriptor.CanonicalName, Configuration, frame);
    public bool Execute(WiredRuntimeContext context)
    {
        if (!ReferenceEquals(context.Room, Instance)) return false;
        context.VariableFrame = WiredVariableRuntimeFrames.Create(context, context.VariableFrame);
        return _executors.Execute(Descriptor.CanonicalName, context.ConfigurationOf(this), context.VariableFrame);
    }
    public void HandleSave(IIncomingPacket packet) => throw new InvalidOperationException("Configured boxes must use validated configuration persistence.");
}
