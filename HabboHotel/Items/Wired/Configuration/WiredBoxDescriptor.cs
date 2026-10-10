namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Names identify behavior; the editor code is the native AIR dialog code within its category.</summary>
public sealed record WiredBoxDescriptor(string CanonicalName, WiredBoxCategory Category, int EditorCode,
    string ConfigurationReference)
{
    public WiredBoxSupport Support { get; init; } = WiredBoxSupport.DescriptorOnly;
    public WiredBoxCategory Envelope => Category;
}
