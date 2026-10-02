namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Names identify behavior; editor codes identify a dialog within its wire envelope.</summary>
public sealed record WiredBoxDescriptor(string CanonicalName, WiredBoxCategory Category, int EditorCode, int TurboCode,
    string ConfigurationReference)
{
    public WiredBoxSupport Support { get; init; } = WiredBoxSupport.DescriptorOnly;
    public WiredBoxCategory Envelope => Category is WiredBoxCategory.Trigger or WiredBoxCategory.Condition
        ? Category : WiredBoxCategory.Action;
}
