using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Native wall coordinates use renderer pixels separately from world altitude.</summary>
internal static class WiredWallGeometry
{
    public static double Altitude(RoomModel model, WiredWallSnapshot position) => CaptureAltitudeInputs(model, position).WorldAltitude;

    internal static WiredWallAltitudeInputs CaptureAltitudeInputs(RoomModel model, WiredWallSnapshot position)
    {
        if (position.NativeAltitude is { } altitude) {
            return new(altitude, 0, 0, 0, 0, position.Left);
        }

        var resolved = ResolveLegacyOrigin(model, position);
        var halfScale = model.Presentation.Scale / 2.0;

        return new(null, halfScale, TileHeight(model, resolved.TileX, resolved.TileY), resolved.LocalX, resolved.PixelY, resolved.Left);
    }

    public static double SavedAltitude(RoomModel model, WiredWallSnapshot snapshot) =>
        (snapshot.NativeAltitude ?? snapshot.CapturedAltitudeHundredths) is { } altitude
            ? altitude / 100.0 : Altitude(model, snapshot);

    public static bool TryProject(RoomModel model, WiredWallSnapshot position, double altitude, out WiredWallSnapshot projected,
        bool canonicalize = true)
    {
        projected = position;

        if (!double.IsFinite(altitude)) {
            return false;
        }

        // Native saved altitude and the a= appendix are integer hundredths.
        var rounded = Math.Floor(altitude * 100 + 0.5);

        if (rounded is < int.MinValue or > int.MaxValue) {
            return false;
        }

        var hundredths = (int)rounded;
        var resolved = ResolveLegacyOrigin(model, position);
        var halfScale = model.Presentation.Scale / 2.0;
        var tileX = resolved.TileX;
        var tileY = resolved.TileY;
        var localX = resolved.LocalX;

        // getOldLocation canonicalizes the same world XY back into tile-local coordinates.
        if (canonicalize && resolved.Left) {
            var shift = (int)Math.Floor(1 - localX / halfScale);
            tileY += shift;
            localX += (int)(shift * halfScale);
        }
        else if (canonicalize) {
            var shift = (int)Math.Floor(localX / halfScale);
            tileX += shift;
            localX -= (int)(shift * halfScale);
        }

        var offset = resolved.Left ? (halfScale - localX) / 2 : localX / 2.0;
        var pixel = (TileHeight(model, tileX, tileY) - hundredths / 100.0) * halfScale + offset;

        if (!canonicalize) {
            // Component writes retain their requested tile/offset, including 0 and halfScale.
            // An implicit legacy (0,0) origin adds a pixel offset which must be inverted as well.
            pixel -= resolved.PixelY - position.PixelY;
            tileX = position.TileX;
            tileY = position.TileY;
            localX = position.LocalX;
        }

        var pixelY = Math.Floor(pixel + 0.5);

        if (pixelY is < int.MinValue or > int.MaxValue) {
            return false;
        }

        projected = new(tileX, tileY, localX, (int)pixelY, resolved.Left, hundredths);

        return projected.IsWithinLimits();
    }

    internal static double TileHeight(RoomModel model, int x, int y)
    {
        if (x < 0 || y < 0 || x >= model.MapSizeX || y >= model.MapSizeY) {
            return 0;
        }

        var wallHeight = (model.WallHeight < 0 ? 0 : model.WallHeight) + 3.6;

        // RoomMessageHandler adds the wall height back to the entrance tile after plane generation.
        if (x == model.DoorX && y == model.DoorY) {
            return model.DoorZ + wallHeight;
        }

        if (model.SqState[x, y] != SquareState.Blocked) {
            return model.SqFloorHeight[x, y];
        }

        var additionalHeight = Math.Min(26, model.WallHeight < 0 ? FloorHeight(model) : model.WallHeight);

        return additionalHeight + wallHeight;
    }

    private static double FloorHeight(RoomModel model)
    {
        var height = 0.0;

        for (var y = 0; y < model.MapSizeY; y++) {
            for (var x = 0; x < model.MapSizeX; x++) {
                if (model.SqState[x, y] != SquareState.Blocked && (x != model.DoorX || y != model.DoorY)) {
                    height = Math.Max(height, model.SqFloorHeight[x, y]);
                }
            }
        }

        return height;
    }

    // Native legacy coordinates (0,0) use the room's first visible wall as an implicit origin.
    private static (int TileX, int TileY, int LocalX, double PixelY, bool Left) ResolveLegacyOrigin(RoomModel model, WiredWallSnapshot position)
    {
        if (position.TileX != 0 || position.TileY != 0) {
            return (position.TileX, position.TileY, position.LocalX, position.PixelY, position.Left);
        }

        var x = model.MapSizeX;
        var y = model.MapSizeY;
        var floor = FloorHeight(model);
        var scale = model.Presentation.Scale;
        var offset = (int)Math.Floor(scale / 10.0 + 0.5);

        if (!position.Left) {
            for (var column = model.MapSizeX - 1; column >= 0; column--) {
                for (var row = 1; row < model.MapSizeY; row++) {
                    if (TileHeight(model, column, row) <= floor) {
                        if (row - 1 < y) {
                            x = column;
                            y = row - 1;
                        }

                        break;
                    }
                }
            }
        }
        else {
            for (var row = model.MapSizeY - 1; row >= 0; row--) {
                for (var column = 1; column < model.MapSizeX; column++) {
                    if (TileHeight(model, column, row) <= floor) {
                        if (column - 1 < x) {
                            x = column - 1;
                            y = row;
                        }

                        break;
                    }
                }
            }
        }

        return (x, y, position.LocalX + (position.Left ? -offset : scale / 2),
            position.PixelY + scale / 4.0 - offset / 2.0, position.Left);
    }
}
