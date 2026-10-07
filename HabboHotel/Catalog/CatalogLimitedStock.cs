using System.Data;
using Dapper;

namespace Plus.HabboHotel.Catalog;

// Limited editions are counted in the database, never from the cached count: a catalog reload can hold an older
// sold count, and two buyers must never get the same serial.
public static class CatalogLimitedStock
{
    // Reserves the next serial of a limited offer; null when it is sold out.
    public static int? Reserve(IDbConnection connection, int offerId)
    {
        if (connection.State != ConnectionState.Open) {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();
        var serial = Reserve(connection, transaction, offerId);

        if (serial == null) {
            return null;
        }

        transaction.Commit();

        return serial;
    }

    internal static int? Reserve(IDbConnection connection, IDbTransaction transaction, int offerId)
    {
        int reserved = connection.Execute("UPDATE catalog_offer_limited SET sold = sold + 1 WHERE offer_id = @offerId AND sold < stack",
            new { offerId }, transaction);

        if (reserved == 0) {
            return null;
        }

        int serial = connection.QuerySingle<int>("SELECT sold FROM catalog_offer_limited WHERE offer_id = @offerId", new { offerId }, transaction);

        return serial;
    }
}
