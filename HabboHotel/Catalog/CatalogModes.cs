namespace Plus.HabboHotel.Catalog;

public static class CatalogModes
{
    public const string Normal = "NORMAL";
    public const string BuildersClub = "BUILDERS_CLUB";

    // Anything the client sends that is not the builders club catalog reads as the normal one.
    public static string FromClient(string? mode) => mode == BuildersClub ? BuildersClub : Normal;
}
