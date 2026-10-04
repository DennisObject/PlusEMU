using Plus.HabboHotel.Catalog.Vouchers;
using Plus.HabboHotel.Users;
using System.Reflection;
using Xunit;

namespace Plus.Tests;

public sealed class VoucherRedemptionServiceTests
{
    [Fact]
    public void SuccessfulClaimPersistsBeforeRewardingWallet()
    {
        var voucher = new Voucher("CREDIT", "credit", 25, 0, 2);
        var habbo = new Habbo { Id = 7, Credits = 10 };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var store = new RecordingStore(() => Assert.Equal(10, habbo.Credits));

        new VoucherRedemptionService(new VoucherManagerFake(voucher), store).Redeem(client, "CREDIT\r");

        Assert.Equal((7, "CREDIT"), Assert.Single(store.Claims));
        Assert.Equal(35, habbo.Credits);
        Assert.Equal(1, voucher.CurrentUses);
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void PersistenceFailureDoesNotRewardOrPublishUsage()
    {
        var voucher = new Voucher("CREDIT", "credit", 25, 0, 2);
        var habbo = new Habbo { Id = 7, Credits = 10 };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var store = new RecordingStore { Fail = true };

        Assert.Throws<InvalidOperationException>(() => new VoucherRedemptionService(new VoucherManagerFake(voucher), store).Redeem(client, "CREDIT"));
        Assert.Equal(10, habbo.Credits);
        Assert.Equal(0, voucher.CurrentUses);
        Assert.Empty(sent);
    }

    [Fact]
    public void DuplicateClaimDoesNotRewardTwice()
    {
        var voucher = new Voucher("DUCKET", "ducket", 5, 0, 2);
        var habbo = new Habbo { Id = 7, Duckets = 3 };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var store = new RecordingStore { DuplicateAfterFirst = true };
        var service = new VoucherRedemptionService(new VoucherManagerFake(voucher), store);

        service.Redeem(client, "DUCKET");
        service.Redeem(client, "DUCKET");

        Assert.Equal(8, habbo.Duckets);
        Assert.Equal(1, voucher.CurrentUses);
        Assert.Equal(3, sent.Count);
    }

    [Fact]
    public void ClosedWalletDoesNotClaimVoucher()
    {
        var voucher = new Voucher("CREDIT", "credit", 25, 0, 2);
        var habbo = new Habbo { Id = 7, Credits = 10 };
        typeof(Habbo).GetField("_habboSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(habbo, true);
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var store = new RecordingStore();

        new VoucherRedemptionService(new VoucherManagerFake(voucher), store).Redeem(client, "CREDIT");

        Assert.Empty(store.Claims);
        Assert.Equal(10, habbo.Credits);
        Assert.Equal(0, voucher.CurrentUses);
        Assert.Empty(sent);
    }

    private sealed class VoucherManagerFake(Voucher voucher) : IVoucherManager
    {
        public void Init() { }
        public bool TryGetVoucher(string code, out Voucher found)
        {
            found = voucher;
            return code == voucher.Code;
        }
    }

    private sealed class RecordingStore(Action? beforeClaim = null) : IVoucherClaimStore
    {
        public bool Fail { get; init; }
        public bool DuplicateAfterFirst { get; init; }
        public VoucherClaimResult Result { get; init; } = VoucherClaimResult.Claimed;
        public List<(int UserId, string Code)> Claims { get; } = [];
        public VoucherClaimResult Claim(int userId, string code)
        {
            beforeClaim?.Invoke();
            if (Fail) throw new InvalidOperationException("forced failure");
            Claims.Add((userId, code));
            if (DuplicateAfterFirst && Claims.Count > 1) return VoucherClaimResult.AlreadyUsed;
            return Result;
        }
    }
}
