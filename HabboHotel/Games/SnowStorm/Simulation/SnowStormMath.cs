namespace Plus.HabboHotel.Games.SnowStorm.Simulation;

/// <summary>
/// Integer math of the official AIR SnowStorm simulation (<c>Direction360</c>, <c>Direction8</c>, <c>QuickRandom</c>,
/// <c>fast_sqrt</c>, <c>javaDiv</c>, <c>Tile</c> conversions). Every lookup table is copied verbatim from AIR.
/// Directions: 360-degree values with 0 = north (-y), 90 = east (+x); Direction8 values 0..7 = N, NE, E, SE, S, SW, W, NW.
/// </summary>
public static class SnowStormMath
{
    public const int TileWidth = 3200;
    public const int TileHalfWidth = 1600;

    private static readonly int[] BaseVectorX =
    [
        0, 4, 8, 13, 17, 22, 26, 31, 35, 40, 44, 48, 53, 57, 61, 66, 70, 74, 79, 83,
        87, 91, 95, 100, 104, 108, 112, 116, 120, 124, 127, 131, 135, 139, 143, 146, 150, 154, 157, 161,
        164, 167, 171, 174, 177, 181, 184, 187, 190, 193, 196, 198, 201, 204, 207, 209, 212, 214, 217, 219,
        221, 223, 226, 228, 230, 232, 233, 235, 237, 238, 240, 242, 243, 244, 246, 247, 248, 249, 250, 251,
        252, 252, 253, 254, 254, 255, 255, 255, 255, 255, 256, 255, 255, 255, 255, 255, 254, 254, 253, 252,
        252, 251, 250, 249, 248, 247, 246, 244, 243, 242, 240, 238, 237, 235, 233, 232, 230, 228, 226, 223,
        221, 219, 217, 214, 212, 209, 207, 204, 201, 198, 196, 193, 190, 187, 184, 181, 177, 174, 171, 167,
        164, 161, 157, 154, 150, 146, 143, 139, 135, 131, 127, 124, 120, 116, 112, 108, 104, 100, 95, 91,
        87, 83, 79, 74, 70, 66, 61, 57, 53, 48, 44, 40, 35, 31, 26, 22, 17, 13, 8, 4,
        0, -4, -8, -13, -17, -22, -26, -31, -35, -40, -44, -48, -53, -57, -61, -66, -70, -74, -79, -83,
        -87, -91, -95, -100, -104, -108, -112, -116, -120, -124, -128, -131, -135, -139, -143, -146, -150, -154, -157, -161,
        -164, -167, -171, -174, -177, -181, -184, -187, -190, -193, -196, -198, -201, -204, -207, -209, -212, -214, -217, -219,
        -221, -223, -226, -228, -230, -232, -233, -235, -237, -238, -240, -242, -243, -244, -246, -247, -248, -249, -250, -251,
        -252, -252, -253, -254, -254, -255, -255, -255, -255, -255, -256, -255, -255, -255, -255, -255, -254, -254, -253, -252,
        -252, -251, -250, -249, -248, -247, -246, -244, -243, -242, -240, -238, -237, -235, -233, -232, -230, -228, -226, -223,
        -221, -219, -217, -214, -212, -209, -207, -204, -201, -198, -196, -193, -190, -187, -184, -181, -177, -174, -171, -167,
        -164, -161, -157, -154, -150, -146, -143, -139, -135, -131, -128, -124, -120, -116, -112, -108, -104, -100, -95, -91,
        -87, -83, -79, -74, -70, -66, -61, -57, -53, -48, -44, -40, -35, -31, -26, -22, -17, -13, -8, -4
    ];

    private static readonly int[] BaseVectorY =
    [
        -256, -255, -255, -255, -255, -255, -254, -254, -253, -252, -252, -251, -250, -249, -248, -247, -246, -244, -243, -242,
        -240, -238, -237, -235, -233, -232, -230, -228, -226, -223, -221, -219, -217, -214, -212, -209, -207, -204, -201, -198,
        -196, -193, -190, -187, -184, -181, -177, -174, -171, -167, -164, -161, -157, -154, -150, -146, -143, -139, -135, -131,
        -128, -124, -120, -116, -112, -108, -104, -100, -95, -91, -87, -83, -79, -74, -70, -66, -61, -57, -53, -48,
        -44, -40, -35, -31, -26, -22, -17, -13, -8, -4, 0, 4, 8, 13, 17, 22, 26, 31, 35, 40,
        44, 48, 53, 57, 61, 66, 70, 74, 79, 83, 87, 91, 95, 100, 104, 108, 112, 116, 120, 124,
        127, 131, 135, 139, 143, 146, 150, 154, 157, 161, 164, 167, 171, 174, 177, 181, 184, 187, 190, 193,
        196, 198, 201, 204, 207, 209, 212, 214, 217, 219, 221, 223, 226, 228, 230, 232, 233, 235, 237, 238,
        240, 242, 243, 244, 246, 247, 248, 249, 250, 251, 252, 252, 253, 254, 254, 255, 255, 255, 255, 255,
        256, 255, 255, 255, 255, 255, 254, 254, 253, 252, 252, 251, 250, 249, 248, 247, 246, 244, 243, 242,
        240, 238, 237, 235, 233, 232, 230, 228, 226, 223, 221, 219, 217, 214, 212, 209, 207, 204, 201, 198,
        196, 193, 190, 187, 184, 181, 177, 174, 171, 167, 164, 161, 157, 154, 150, 146, 143, 139, 135, 131,
        128, 124, 120, 116, 112, 108, 104, 100, 95, 91, 87, 83, 79, 74, 70, 66, 61, 57, 53, 48,
        44, 40, 35, 31, 26, 22, 17, 13, 8, 4, 0, -4, -8, -13, -17, -22, -26, -31, -35, -40,
        -44, -48, -53, -57, -61, -66, -70, -74, -79, -83, -87, -91, -95, -100, -104, -108, -112, -116, -120, -124,
        -128, -131, -135, -139, -143, -146, -150, -154, -157, -161, -164, -167, -171, -174, -177, -181, -184, -187, -190, -193,
        -196, -198, -201, -204, -207, -209, -212, -214, -217, -219, -221, -223, -226, -228, -230, -232, -233, -235, -237, -238,
        -240, -242, -243, -244, -246, -247, -248, -249, -250, -251, -252, -252, -253, -254, -254, -255, -255, -255, -255, -255
    ];

    private static readonly int[] ComponentToAngle =
    [
        0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4,
        4, 5, 5, 5, 5, 6, 6, 6, 6, 6, 7, 7, 7, 7, 8, 8, 8, 8, 8, 9,
        9, 9, 9, 10, 10, 10, 10, 10, 11, 11, 11, 11, 12, 12, 12, 12, 12, 13, 13, 13,
        13, 13, 14, 14, 14, 14, 15, 15, 15, 15, 15, 16, 16, 16, 16, 16, 17, 17, 17, 17,
        17, 18, 18, 18, 18, 18, 19, 19, 19, 19, 19, 20, 20, 20, 20, 20, 21, 21, 21, 21,
        21, 22, 22, 22, 22, 22, 23, 23, 23, 23, 23, 24, 24, 24, 24, 24, 24, 25, 25, 25,
        25, 25, 26, 26, 26, 26, 26, 26, 27, 27, 27, 27, 27, 28, 28, 28, 28, 28, 28, 29,
        29, 29, 29, 29, 29, 30, 30, 30, 30, 30, 30, 31, 31, 31, 31, 31, 31, 32, 32, 32,
        32, 32, 32, 33, 33, 33, 33, 33, 33, 34, 34, 34, 34, 34, 34, 34, 35, 35, 35, 35,
        35, 35, 36, 36, 36, 36, 36, 36, 36, 37, 37, 37, 37, 37, 37, 37, 38, 38, 38, 38,
        38, 38, 38, 39, 39, 39, 39, 39, 39, 39, 39, 40, 40, 40, 40, 40, 40, 40, 41, 41,
        41, 41, 41, 41, 41, 41, 42, 42, 42, 42, 42, 42, 42, 42, 43, 43, 43, 43, 43, 43,
        43, 43, 44, 44, 44, 44, 44, 44, 44, 44, 44, 45, 45, 45, 45, 45
    ];

    private static readonly int[] SqrtTable =
    [
        0, 16, 22, 27, 32, 35, 39, 42, 45, 48, 50, 53, 55, 57, 59, 61, 64, 65, 67, 69,
        71, 73, 75, 76, 78, 80, 81, 83, 84, 86, 87, 89, 90, 91, 93, 94, 96, 97, 98, 99,
        101, 102, 103, 104, 106, 107, 108, 109, 110, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122,
        123, 124, 125, 126, 128, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142,
        143, 144, 144, 145, 146, 147, 148, 149, 150, 150, 151, 152, 153, 154, 155, 155, 156, 157, 158, 159,
        160, 160, 161, 162, 163, 163, 164, 165, 166, 167, 167, 168, 169, 170, 170, 171, 172, 173, 173, 174,
        175, 176, 176, 177, 178, 178, 179, 180, 181, 181, 182, 183, 183, 184, 185, 185, 186, 187, 187, 188,
        189, 189, 190, 191, 192, 192, 193, 193, 194, 195, 195, 196, 197, 197, 198, 199, 199, 200, 201, 201,
        202, 203, 203, 204, 204, 205, 206, 206, 207, 208, 208, 209, 209, 210, 211, 211, 212, 212, 213, 214,
        214, 215, 215, 216, 217, 217, 218, 218, 219, 219, 220, 221, 221, 222, 222, 223, 224, 224, 225, 225,
        226, 226, 227, 227, 228, 229, 229, 230, 230, 231, 231, 232, 232, 233, 234, 234, 235, 235, 236, 236,
        237, 237, 238, 238, 239, 240, 240, 241, 241, 242, 242, 243, 243, 244, 244, 245, 245, 246, 246, 247,
        247, 248, 248, 249, 249, 250, 250, 251, 251, 252, 252, 253, 253, 254, 254, 255
    ];

    /// <summary>AIR <c>class_4083.javaDiv</c>: truncates toward zero.</summary>
    public static int JavaDiv(double value) => value >= 0 ? (int)Math.Floor(value) : (int)Math.Ceiling(value);

    /// <summary>AIR <c>class_4035.fast_sqrt</c>: table-driven integer square root; -1 for negative input.</summary>
    public static int FastSqrt(int value)
    {
        if (value >= 65536) {
            if (value >= 16777216) {
                if (value >= 268435456) {
                    if (value >= 1073741824) {
                        return SqrtTable[value >> 24] << 8;
                    }

                    return SqrtTable[value >> 22] << 7;
                }

                if (value >= 67108864) {
                    return SqrtTable[value >> 20] << 6;
                }

                return SqrtTable[value >> 18] << 5;
            }

            if (value >= 1048576) {
                if (value >= 4194304) {
                    return SqrtTable[value >> 16] << 4;
                }

                return SqrtTable[value >> 14] << 3;
            }

            if (value >= 262144) {
                return SqrtTable[value >> 12] << 2;
            }

            return SqrtTable[value >> 10] << 1;
        }

        if (value >= 256) {
            if (value >= 4096) {
                if (value >= 16384) {
                    return SqrtTable[value >> 8];
                }

                return SqrtTable[value >> 6] >> 1;
            }

            if (value >= 1024) {
                return SqrtTable[value >> 4] >> 2;
            }

            return SqrtTable[value >> 2] >> 3;
        }

        if (value >= 0) {
            return SqrtTable[value] >> 4;
        }

        return -1;
    }

    /// <summary>AIR <c>QuickRandom.iterateSeed</c> (xorshift, arithmetic right shift); seeds the per-turn checksum.</summary>
    public static int IterateSeed(int seed)
    {
        if (seed == 0) {
            seed = -1;
        }

        seed ^= seed << 13;
        seed ^= seed >> 17;

        return seed ^ (seed << 5);
    }

    public static int ValidateDirection360(int value)
    {
        if (value > 359) {
            value %= 360;
        }
        else if (value < 0) {
            value = 360 + value % 360;
        }

        return value;
    }

    public static int Direction360ToDirection8(int value) => ((ValidateDirection360(value - 22) / 45) + 1) & 7;

    public static int Direction8ToDirection360(int direction8) => direction8 is >= 0 and <= 7 ? direction8 * 45 : -1;

    public static int RotateDirection8(int direction8, int steps) => (direction8 + steps) & 7;

    public static int BaseVectorXComponent(int direction360) => BaseVectorX[ValidateDirection360(direction360)];

    public static int BaseVectorYComponent(int direction360) => BaseVectorY[ValidateDirection360(direction360)];

    /// <summary>AIR <c>Direction360.getAngleFromComponents</c>: table-driven atan2, may return 360 for due north.</summary>
    public static int GetAngleFromComponents(int x, int y)
    {
        int index;

        if (Abs(x) <= Abs(y)) {
            if (y == 0) {
                y = 1;
            }

            x *= 256;
            index = Abs(x / y);

            if (index > 255) {
                index = 255;
            }

            if (y < 0) {
                return x > 0 ? ComponentToAngle[index] : 360 - ComponentToAngle[index];
            }

            return x > 0 ? 180 - ComponentToAngle[index] : 180 + ComponentToAngle[index];
        }

        if (x == 0) {
            x = 1;
        }

        y *= 256;
        index = Abs(y / x);

        if (index > 255) {
            index = 255;
        }

        if (y < 0) {
            return x > 0 ? 90 - ComponentToAngle[index] : 270 + ComponentToAngle[index];
        }

        return x > 0 ? 90 + ComponentToAngle[index] : 270 - ComponentToAngle[index];
    }

    /// <summary>AIR <c>Tile.convertToTileX/Y</c>: world units to the nearest tile index.</summary>
    public static int WorldToTile(int world) => (world + TileHalfWidth) / TileWidth;

    public static int TileToWorld(int tile) => tile * TileWidth;

    /// <summary>AIR <c>Location3D.isInDistanceStatic</c>: box pre-check, then strict circle test.</summary>
    public static bool IsInDistance(int x1, int y1, int x2, int y2, int distance)
    {
        int dx = Abs(x2 - x1);
        int dy = Abs(y2 - y1);

        if (dy > distance || dx > distance) {
            return false;
        }

        return dx * dx + dy * dy < distance * distance;
    }

    // AIR absoluteValue: wraps on int.MinValue instead of throwing.
    internal static int Abs(int value) => value < 0 ? -value : value;
}
