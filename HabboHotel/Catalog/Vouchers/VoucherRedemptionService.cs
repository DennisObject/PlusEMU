using Dapper;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.Database;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Catalog.Vouchers;

public enum VoucherClaimResult { Claimed, AlreadyUsed, Exhausted }

public interface IVoucherClaimStore
{
    VoucherClaimResult Claim(int userId, string code);
}

public sealed class VoucherClaimStore(IDatabase database) : IVoucherClaimStore
{
    public VoucherClaimResult Claim(int userId, string code)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (connection.Execute("INSERT IGNORE INTO user_vouchers (user_id,voucher) VALUES (@userId,@code)", new { userId, code }, transaction) != 1)
            return VoucherClaimResult.AlreadyUsed;
        if (connection.Execute("UPDATE catalog_vouchers SET current_uses=current_uses+1 WHERE voucher=@code AND enabled=1 AND current_uses<max_uses LIMIT 1",
                new { code }, transaction) != 1)
            return VoucherClaimResult.Exhausted;
        transaction.Commit();
        return VoucherClaimResult.Claimed;
    }
}

public interface IVoucherRedemptionService
{
    void Redeem(GameClient session, string code);
}

public sealed class VoucherRedemptionService(IVoucherManager vouchers, IVoucherClaimStore claims) : IVoucherRedemptionService
{
    public void Redeem(GameClient session, string code)
    {
        var habbo = session.GetHabbo();
        code = code.Replace("\r", "");
        if (!vouchers.TryGetVoucher(code, out var voucher))
        {
            session.Send(new VoucherRedeemErrorComposer(VoucherRedeemError.InvalidCode));
            return;
        }

        lock (habbo.WalletSync)
        {
            if (habbo.WalletClosed) return;
            lock (voucher)
            {
                switch (claims.Claim(habbo.Id, code))
                {
                    case VoucherClaimResult.AlreadyUsed:
                        session.SendNotification("You've already used this voucher code, one per each user, sorry!");
                        return;
                    case VoucherClaimResult.Exhausted:
                        session.SendNotification("Oops, this voucher has reached the maximum usage limit!");
                        return;
                }
                voucher.MarkUsed();
            }
            if (voucher.Type == VoucherType.Credit)
            {
                habbo.Credits += voucher.Value;
                session.Send(new CreditBalanceComposer(habbo.Credits));
            }
            else if (voucher.Type == VoucherType.Ducket)
            {
                habbo.Duckets += voucher.Value;
                session.Send(new HabboActivityPointNotificationComposer(habbo.Duckets, voucher.Value));
            }
            session.Send(new VoucherRedeemOkComposer());
        }
    }
}
