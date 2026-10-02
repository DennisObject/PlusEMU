namespace Plus.Communication.Packets;

/// <summary>
/// Floor-plan wire ids. <see cref="September"/> is the WIN63-202609091217-117204808
/// editor. The Octane hybrid revision speaks those ids and is not a full official client.
/// <see cref="Legacy"/> remains the NITRO-1-6-6 revision.
/// <see cref="StockRendererRevision"/> is the unmodified renderer hello and uses that same mapping.
/// </summary>
public static class FloorPlanWire
{
    public const string HybridRevision = "OCTANE-3-6-0-FLOOR-20260909";
    public const string LegacyRevision = "NITRO-1-6-6";
    public const string StockRendererRevision = "NITRO-3-6-0";
    public const uint MarketplaceAlias = 65001;

    public readonly record struct Headers(
        uint GetOccupiedTiles,
        uint GetRoomEntryTile,
        uint UpdateFloorProperties,
        uint RoomOccupiedTiles,
        uint RoomEntryTile,
        uint RoomVisualizationSettings,
        uint FloorHeightMap);

    public static Headers Legacy { get; } = new(1687, 3559, 875, 3990, 1664, 3547, 1301);

    public static Headers September { get; } = new(2597, 2735, 234, 2757, 2959, 1392, 1589);
}
