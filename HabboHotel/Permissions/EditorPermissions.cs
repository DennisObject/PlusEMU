using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Permissions;

// Rights for Octane's in-client catalog and furni editors. The server checks them on every packet;
// the permission map the client receives only decides whether the editors are offered.
public static class EditorPermissions
{
    public const string CatalogFurni = "acc_catalogfurni";
    public const string FurnidataEdit = "acc_furnidata_edit";
    public const string FurniDelete = "acc_furni_delete";

    public static bool Has(Habbo? habbo, string right) => habbo?.Permissions?.HasRight(right) == true;

    // Every editor action needs acc_catalogfurni; the destructive ones also need their own right.
    public static bool Allows(Habbo? habbo, string? extraRight = null) =>
        Has(habbo, CatalogFurni) && (extraRight == null || Has(habbo, extraRight));
}
