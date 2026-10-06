using Dapper;
using Plus.Core;
using System.Diagnostics.CodeAnalysis;
using Plus.Database;

namespace Plus.HabboHotel.Catalog.Vouchers;

public class VoucherManager : IVoucherManager, IStartable
{
    private readonly IDatabase _database;
    private readonly Dictionary<string, Voucher> _vouchers;

    public VoucherManager(IDatabase database)
    {
        _database = database;
        _vouchers = new();
    }

    public int StartOrder => 20;
    public Task Start() => Load();
    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var vouchers = await connection.QueryAsync<Voucher>("SELECT voucher AS Code, type, value, current_uses AS CurrentUses, max_uses AS MaxUses FROM catalog_vouchers WHERE enabled = TRUE");
        _vouchers.Clear();

        foreach (var voucher in vouchers) {
            _vouchers.Add(voucher.Code, voucher);
        }
    }

    public bool TryGetVoucher(string code, [NotNullWhen(true)] out Voucher? voucher) => _vouchers.TryGetValue(code, out voucher);
}
