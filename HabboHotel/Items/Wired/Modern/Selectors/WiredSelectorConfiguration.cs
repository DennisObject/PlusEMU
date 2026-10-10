using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Modern.Selectors;

public static class WiredSelectorConfiguration
{
    public static WiredConfiguration Normalize(string name, WiredConfiguration configuration)
    {
        if (!WiredSelectorModule.Names.Contains(name, StringComparer.Ordinal) || !WiredLegacyProtocol.IsWithinLimits(configuration)) {
            throw new ArgumentException("Invalid selector configuration", nameof(configuration));
        }

        return configuration;
    }
}
