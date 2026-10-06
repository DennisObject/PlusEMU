using System.Diagnostics.CodeAnalysis;
namespace Plus.HabboHotel.Catalog.Vouchers;

public interface IVoucherManager
{
    void Init();
    bool TryGetVoucher(string code, [NotNullWhen(true)] out Voucher? voucher);
}