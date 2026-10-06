using System.Data;
using Dapper;

namespace Plus.HabboHotel.Catalog;

// Limited editions are counted in the database, never from the cached count: a catalog reload can hold an older
// limited_sells, and two buyers must never get the same serial.
public static class CatalogLimitedStock
{
    // Reserves the next serial of a limited offer; null when it is sold out.
    public static int? Reserve(IDbConnection connection, int offerRowId)
    {
        if (connection.State != ConnectionState.Open) {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();
        var serial = Reserve(connection, transaction, offerRowId);

        if (serial == null) {
            return null;
        }

        transaction.Commit();

        return serial;
    }

    internal static int? Reserve(IDbConnection connection, IDbTransaction transaction, int offerRowId)
    {
        int reserved = connection.Execute("UPDATE catalog_items SET limited_sells = limited_sells + 1 WHERE id = @offerRowId AND limited_sells < limited_stack",
            new { offerRowId }, transaction);

        if (reserved == 0) {
            return null;
        }

        int serial = connection.QuerySingle<int>("SELECT limited_sells FROM catalog_items WHERE id = @offerRowId", new { offerRowId }, transaction);

        return serial;
    }
}
