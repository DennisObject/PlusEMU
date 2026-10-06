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

    public string Code
    {
        get; set;
    }

    public VoucherType Type
    {
        get; set;
    }

    public int Value
    {
        get; set;
    }

    public int CurrentUses
    {
        get; set;
    }

    public int MaxUses
    {
        get; set;
    }

    public void MarkUsed() => CurrentUses++;
}
