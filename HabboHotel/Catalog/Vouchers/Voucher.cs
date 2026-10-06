using Dapper;

namespace Plus.HabboHotel.Catalog.Vouchers;

public class Voucher
{
    public Voucher(string code, string type, int value, int currentUses, int maxUses)
    {
        Code = code;
        Type = VoucherUtility.GetType(type);
        Value = value;
        CurrentUses = currentUses;
        MaxUses = maxUses;
    }

    public string Code { get; set; }

    public VoucherType Type { get; set; }

    public int Value { get; set; }

    public int CurrentUses { get; set; }

    public int MaxUses { get; set; }

    public void UpdateUses()
    {
        CurrentUses += 1;
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        connection.Execute("UPDATE catalog_vouchers SET current_uses=current_uses+1 WHERE voucher=@code LIMIT 1", new { code = Code });
    }
}
