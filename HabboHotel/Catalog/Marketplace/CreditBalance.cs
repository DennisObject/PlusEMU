namespace Plus.HabboHotel.Catalog.Marketplace;

/// <summary>Adds a grant to a balance, or returns null when it would overflow the int wire field.</summary>
public static class CreditBalance
{
    public static int? AddToBalance(int balance, int amount)
    {
        var total = (long)balance + amount;

        return total is < 0 or > int.MaxValue ? null : (int)total;
    }
}
