using System.Collections.Concurrent;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Passive speech-capture configuration, consumed before the speech trigger's literal predicate.</summary>
public sealed class WiredVariableTextInputBox(Room room, Item item, WiredBoxDescriptor descriptor) : IWiredConfiguredItem
{
    public Room Instance { get; set; } = room;
    public Item Item { get; set; } = item;
    public WiredBoxDescriptor Descriptor { get; } = descriptor with { Support = WiredBoxSupport.Implemented };
    public WiredConfiguration Configuration { get; private set; } = new() { IntParams = [1], Text = "\t" };
    public WiredBoxType Type => WiredBoxType.None;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
    public string StringData { get; set; } = "";
    public string ItemsData { get; set; } = "";
    public bool BoolData { get; set; }
    public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        validated = proposed; error = "Choose a context variable and capturer name.";
        var parts = proposed.Text.Split('\t');
        if (proposed.Version != 1 || proposed.IntParams.Length != 1 || proposed.IntParams[0] is not (1 or 2)
            || parts.Length != 2 || !WiredVariableModule.TryDefinitionId(parts[0], out _)) return false;
        var name = parts[1].Trim(); if (name.Length >= 2 && name.StartsWith('#') && name.EndsWith('#')) name = name[1..^1].Trim();
        if (name.Length is < 1 or > 32 || name.IndexOfAny(['\r', '\n', '#']) >= 0) return false;
        validated = proposed with { Text = parts[0] + "\t" + name }; error = ""; return true;
    }
    public void ApplyConfiguration(WiredConfiguration validated) { Configuration = validated; StringData = validated.Text; }
    public void HandleSave(IIncomingPacket packet) => throw new InvalidOperationException("Capture boxes require validated persistence.");
    public bool Execute(params object[] arguments) => false;
}
