using Plus.HabboHotel.Catalog.Vouchers;

namespace Plus.Communication.RCON.Commands.Hotel;

internal class ReloadVouchersCommand : IRconCommand
{
    private readonly IVoucherManager _voucherManager;
    public string Description => "This command is used to reload the voucher manager.";

    public string Key => "reload_vouchers";
    public string Parameters => "";

    public ReloadVouchersCommand(IVoucherManager voucherManager)
    {
        _voucherManager = voucherManager;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        _voucherManager.Init();

        return Task.FromResult(true);
    }
}
