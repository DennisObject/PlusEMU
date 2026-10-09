namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Outer payload bounds; concrete boxes impose their own smaller field and selection limits.</summary>
public static class WiredConfigurationLimits
{
    // Neighborhood selectors save six fields followed by all 81 editor tile pairs.
    public const int NeighborhoodTiles = 81;
    public const int IntParams = 6 + NeighborhoodTiles * 2;
    public const int SelectedItems = 100;
    public const int TextCharacters = 32768;
    public const int DelayPulses = 3600;
    public const int SelectionCode = 2;
}
