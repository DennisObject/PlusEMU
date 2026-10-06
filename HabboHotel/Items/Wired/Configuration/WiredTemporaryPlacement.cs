namespace Plus.HabboHotel.Items.Wired.Configuration;

// These two choices follow Turbo's explicit placement enums, rather than Octane's six definition-editor slots.
public enum WiredPlaceLocationType
{
    SourceLocation = 0, CustomLocation = 1
}
public enum WiredPlaceAltitudeType
{
    OnTopOfTargetLocation = 0, SourceAltitude = 1, CustomAltitude = 2
}

/// <summary>Optional snapshot/relative placement settings stored only in the companion configuration.</summary>
public sealed record WiredTemporaryPlacement(
    bool TargetIsUser = false,
    WiredPlaceLocationType Location = WiredPlaceLocationType.SourceLocation,
    WiredPlaceAltitudeType Altitude = WiredPlaceAltitudeType.OnTopOfTargetLocation,
    int OffsetX = 0, int OffsetY = 0, int OffsetAltitudeHundredths = 0,
    bool SpawnWithVariable = false, bool ValueIsVariable = false, int Value = 0,
    // Active Octane/domain targets: User0, Furni1, Context2, Global3; never Turbo's 0/1/-20/-10 values.
    int ValueTarget = 0)
{
    public bool IsWithinLimits() => Enum.IsDefined(Location) && Enum.IsDefined(Altitude)
        && OffsetX is >= -64 and <= 64 && OffsetY is >= -64 and <= 64
        && OffsetAltitudeHundredths is >= -8000 and <= 8000 && ValueTarget is >= 0 and <= 3;
}
