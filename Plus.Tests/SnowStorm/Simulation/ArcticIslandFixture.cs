using Plus.HabboHotel.Games.SnowStorm.Simulation;

namespace Plus.Tests.SnowStorm.Simulation;

// Official Arctic Island (arena 8) copied from Polaris origin/dev
// V20260729230000__snowwar_official_arctic_island.sql, mapped to FuseObjectData the way the SnowStorm server does:
// ids 1..n in file order, ads_background and spawn lines dropped, snowball_machine -> s_snowball_machine (1x1, height 2400),
// snst_fence/ads_igorraygun 1x2, height = Polaris collision height, altitude from z=, everything canStandOn = false.
internal static class ArcticIslandFixture
{
    public const string HeightMapRows = """
        xxxxxxxxxxxxxxxxxxx00000xxxxxxxxxxxxxxxxxxxxxxxxxx
        xxxxxxxxxxxxxxxxxx0000000xxxxxxxxxxxxxxxxxxxxxxxxx
        xxxxxxxxxxxxxxxxx000000000xxxxxxxxxxxxxxxxxxxxxxxx
        xxxxxxxxxxxxxxxx00000000000xxxxxxxxxxxxxxxxxxxxxxx
        xxxxxxxxxxxxxxx0000000000000xxxxxxxxxxxxxxxxxxxxxx
        xxxxxxxxxxxxxx000000000000000xxxxxxxxxxxxxxxxxxxxx
        xxxxxxxxxxxxx00000000000000000xxxxxxxxxxxxxxxxxxxx
        xxxxxxxxxxxx0000000000000000000xxxxxxxxxxxxxxxxxxx
        xxxxxxxxxxx000000000000000000000xxxxxxxxxxxxxxxxxx
        xxxxxxxxxx00000000000000000000000xxxxxxxxxxxxxxxxx
        xxxxxxxxx0000000000000000000000000xxxxxxxxxxxxxxxx
        xxxxxxxx000000000000000000000000000xxxxxxxxxxxxxxx
        xxxxxxx00000000000000000000000000000xxxxxxxxxxxxxx
        xxxxxx0000000000000000000000000000000xxxxxxxxxxxxx
        xxxxx000000000000000000000000000000000xxxxxxxxxxxx
        xxxx00000000000000000000000000000000000xxxxxxxxxxx
        xxx0000000000000000000000000000000000000xxxxxxxxxx
        xx000000000000000000000000000000000000000xxxxxxxxx
        x00000000000000000000000000000000000000000xxxxxxxx
        00000000000000000000xxxxx0xxxxxxx0000000000xxxxxxx
        00000000000000000000xxxxx0xxxxxxx00000000000xxxxxx
        00000000000000000000xxxxx0xxxxxxx000000000000xxxxx
        00000000000000000000xxx0000000xxx0000000000000xxxx
        x0000000000000000000xxx0000000xxx00000000000000xxx
        xx000000000000000000xxx0000000000000000000000000xx
        xxx00000000000000000xxx0000000xxx0000000000000000x
        xxxx00000000000000000000000000xxx00000000000000000
        xxxxx000000000000000xxx0000000xxx00000000000000000
        xxxxxx00000000000000xxxxxxx0xxxxx00000000000000000
        xxxxxxx0000000000000xxxxxxx0xxxxx00000000000000000
        xxxxxxxx000000000000xxxxxxx0xxxxx00000000000000000
        xxxxxxxxx00000000000000000000000000000000000000000
        xxxxxxxxxx000000000000000000000000000000000000000x
        xxxxxxxxxxx0000000000000000000000000000000000000xx
        xxxxxxxxxxxx00000000000000000000000000000000000xxx
        xxxxxxxxxxxxx000000000000000000000000000000000xxxx
        xxxxxxxxxxxxxx0000000000000000000000000000000xxxxx
        xxxxxxxxxxxxxxx00000000000000000000000000000xxxxxx
        xxxxxxxxxxxxxxxx000000000000000000000000000xxxxxxx
        xxxxxxxxxxxxxxxxx0000000000000000000000000xxxxxxxx
        xxxxxxxxxxxxxxxxxx00000000000000000000000xxxxxxxxx
        xxxxxxxxxxxxxxxxxxx000000000000000000000xxxxxxxxxx
        xxxxxxxxxxxxxxxxxxxx0000000000000000000xxxxxxxxxxx
        xxxxxxxxxxxxxxxxxxxxx00000000000000000xxxxxxxxxxxx
        xxxxxxxxxxxxxxxxxxxxxx000000000000000xxxxxxxxxxxxx
        xxxxxxxxxxxxxxxxxxxxxxx0000000000000xxxxxxxxxxxxxx
        xxxxxxxxxxxxxxxxxxxxxxxx00000000000xxxxxxxxxxxxxxx
        xxxxxxxxxxxxxxxxxxxxxxxxx000000000xxxxxxxxxxxxxxxx
        xxxxxxxxxxxxxxxxxxxxxxxxxx0000000xxxxxxxxxxxxxxxxx
        xxxxxxxxxxxxxxxxxxxxxxxxxxx00000xxxxxxxxxxxxxxxxxx
        """;

    public const string Items = """
        snst_block1 41 37 0 3 2300 0
        snst_block1 2 20 0 3 2300 0 z=1440
        snst_block1 10 18 6 3 2300 0 z=1440
        snst_tree1 19 41 0 3 2300 0
        snst_block1 41 20 0 3 2300 0
        snst_tree1 9 31 0 3 2300 0
        snst_block1 23 44 0 3 2300 0 z=1440
        snst_block1 4 18 0 3 2300 0
        snst_block1 2 20 0 3 2300 0
        snst_tree1 25 38 0 3 2300 0
        snst_block1 2 19 0 3 2300 0
        snst_block1 9 18 0 3 2300 0
        snst_block1 7 18 0 3 2300 0
        snst_fence 17 14 2 3 0 0
        snst_block1 2 18 0 3 2300 0
        snst_tree1 49 28 0 3 2300 0
        snst_block1 2 22 6 3 2300 0
        snst_block1 5 18 0 3 2300 0
        snst_block1 4 18 0 3 2300 0 z=1440
        snst_block1 39 22 2 3 2300 0 z=1440
        snst_block1 39 23 0 3 2300 0
        snst_fence 24 44 0 3 0 0
        snst_tree1 36 15 0 3 2300 0
        snst_block1 23 44 0 3 2300 0
        snst_tree1 47 32 0 3 2300 0
        snst_block1 39 37 0 3 2300 0
        snst_block1 8 18 0 3 2300 0
        snst_block1 2 19 0 3 2300 0 z=1440
        snst_tree1 6 20 0 3 2300 0
        snst_block1 2 18 0 3 2300 0 z=1440
        snst_fence 15 14 2 3 0 0
        snst_tree1 26 6 0 3 2300 0
        snst_block1 39 23 4 3 2300 0 z=1440
        snst_block1 23 38 2 3 2300 0 z=1440
        snst_tree1 10 26 0 3 2300 0
        snst_block1 23 45 4 3 2300 0
        snst_block1 39 21 0 3 2300 0 z=1440
        snst_block1 5 18 0 3 2300 0 z=1440
        snst_block1 39 24 6 3 2300 0 z=1440
        snst_tree1 13 15 0 3 2300 0
        snst_block1 3 18 0 3 2300 0 z=1440
        snst_fence 13 14 2 3 0 0
        snst_tree1 30 7 0 3 2300 0
        snst_block1 40 20 0 3 2300 0
        snst_fence 29 7 0 3 0 0
        snst_fence 21 14 2 3 0 0
        snst_fence 24 40 0 3 0 0
        snst_block1 23 40 0 3 2300 0 z=1440
        snst_tree1 15 10 0 3 2300 0
        snst_block1 37 37 0 3 2300 0
        snst_tree1 20 4 0 3 2300 0
        snst_block1 3 18 0 3 2300 0
        snst_block1 23 40 0 3 2300 0
        snst_block1 2 22 2 3 2300 0 z=1440
        snst_block1 43 20 6 3 2300 0 z=1440
        snst_block1 23 39 0 3 2300 0
        snst_tree1 45 25 0 3 2300 0
        snst_block1 23 42 0 3 2300 0
        snst_block1 42 20 0 3 2300 0
        snst_block1 9 18 0 3 2300 0 z=1440
        snst_block1 10 18 0 3 2300 0
        snst_fence 24 38 0 3 0 0
        snst_block1 11 18 0 3 2300 0
        snst_block1 39 37 0 3 2300 0 z=1440
        snst_block1 8 18 0 3 2300 0 z=1440
        snst_block1 39 20 0 3 2300 0
        snst_block1 38 37 0 3 2300 0
        snst_block1 40 37 0 3 2300 0 z=1440
        snst_block1 2 21 0 3 2300 0
        snst_fence 24 42 0 3 0 0
        snst_block1 39 22 0 3 2300 0
        snst_block1 38 37 0 3 2300 0 z=1440
        snst_block1 40 20 0 3 2300 0 z=1440
        snst_tree1 28 47 0 3 2300 0
        snst_block1 6 18 0 3 2300 0
        snst_tree1 5 24 0 3 2300 0
        snst_block1 41 20 4 3 2300 0 z=1440
        snst_fence 19 14 2 3 0 0
        snst_tree1 20 8 0 3 2300 0
        snst_block1 2 21 0 3 2300 0 z=1440
        snst_block1 23 38 0 3 2300 0
        snst_block1 23 42 0 3 2300 0 z=1440
        snst_block1 42 20 0 3 2300 0 z=1440
        snst_block1 39 21 0 3 2300 0
        snst_block1 12 18 4 3 2300 0
        snst_block1 11 18 0 3 2300 0 z=1440
        snst_block1 40 37 0 3 2300 0
        snst_block1 23 41 0 3 2300 0
        snst_block1 43 20 2 3 2300 0
        snst_block1 39 20 0 3 2300 0 z=1440
        snst_tree1 15 34 0 3 2300 0
        snst_block1 6 18 0 3 2300 0 z=1440
        snst_block1 7 18 0 3 2300 0 z=1440
        snst_fence 29 9 0 3 0 0
        snst_block1 39 24 2 3 2300 0
        snst_block1 23 43 0 3 2300 0
        ads_igorraygun 28 12 4 3 230 0
        ads_igorraygun 41 33 6 3 230 0
        ads_igorraygun 31 41 0 3 230 0
        ads_igorraygun 17 37 2 3 230 0
        ads_igorraygun 11 21 2 3 230 0
        snowball_machine 26 24
        """;

    // Polaris spawn lines: the first five are the north team zone, the last five the south zone.
    public static readonly (int X, int Y)[] NorthSpawns = [(22, 9), (25, 12), (26, 8), (31, 14), (23, 13)];

    public static readonly (int X, int Y)[] SouthSpawns = [(30, 43), (33, 42), (38, 41), (26, 42), (33, 46)];

    public static SnowStormLevelData Level()
    {
        string[] rows = HeightMapRows.Split('\n');
        var fuseObjects = new List<SnowStormFuseObject>();

        foreach (string line in Items.Split('\n')) {
            string[] tokens = line.Split(' ');
            int id = fuseObjects.Count + 1;

            if (tokens[0] == "snowball_machine") {
                fuseObjects.Add(new SnowStormFuseObject("s_snowball_machine", id, int.Parse(tokens[1]), int.Parse(tokens[2]), 1, 1, 2400, 0, 0, false, "0"));
                continue;
            }

            bool twoTiles = tokens[0] is "snst_fence" or "ads_igorraygun";
            int altitude = tokens.Length > 7 && tokens[7].StartsWith("z=") ? int.Parse(tokens[7][2..]) : 0;
            fuseObjects.Add(new SnowStormFuseObject(tokens[0], id, int.Parse(tokens[1]), int.Parse(tokens[2]), 1, twoTiles ? 2 : 1,
                int.Parse(tokens[5]), int.Parse(tokens[3]), altitude, false, tokens[6]));
        }

        return new SnowStormLevelData(rows[0].Length, rows.Length, string.Join('\r', rows), fuseObjects);
    }
}
