namespace Plus.HabboHotel.Rooms;

internal static class RoomPosture
{
    public static bool ReleaseSit(bool isSitting, bool hasSitStatus, bool tileHasSeat)
        => hasSitStatus && !isSitting && !tileHasSeat;

    public static bool ReleaseLay(bool isLying, bool hasLayStatus, bool tileHasBed)
        => hasLayStatus && !isLying && !tileHasBed;
}
