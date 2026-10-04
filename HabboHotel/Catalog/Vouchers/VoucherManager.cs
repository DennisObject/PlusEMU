using System.Diagnostics.CodeAnalysis;
using System.Data;
using Plus.Database;

namespace Plus.HabboHotel.Catalog.Vouchers;

public class VoucherManager : IVoucherManager
{
    private readonly IDatabase _database;
    private readonly Dictionary<string, Voucher> _vouchers;

    public VoucherManager(IDatabase database)
    {
        _database = database;
        _vouchers = new();
    }

    public void Init()
    {
        if (_vouchers.Count > 0)
            _vouchers.Clear();
        DataTable? data = null;
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.SetQuery("SELECT `voucher`,`type`,`value`,`current_uses`,`max_uses` FROM `catalog_vouchers` WHERE `enabled` = '1'");
            data = dbClient.GetTable();
        }
        if (data != null)
        {
            foreach (DataRow row in data.Rows)
            {
                _vouchers.Add(Convert.ToString(row["voucher"]),
                    new(Convert.ToString(row["voucher"]), Convert.ToString(row["type"]), Convert.ToInt32(row["value"]), Convert.ToInt32(row["current_uses"]),
                        Convert.ToInt32(row["max_uses"])));
            }
        }
    }

    public bool TryGetVoucher(string code, [NotNullWhen(true)] out Voucher? voucher) => _vouchers.TryGetValue(code, out voucher);
}