using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public class LoginServiceTests
{
    private static readonly Argon2idPasswordHasher Hasher = new();

    private readonly FakeAccounts _accounts = new();
    private readonly FakeSsoTickets _tickets = new();
    private readonly FakeAccessTokens _tokens = new();
    private readonly FakeModeration _moderation = new();
    private readonly LoginThrottle _throttle = new(TimeProvider.System, AuthTestConfig.Options(c => c.MaxFailedLoginsPerAccount = 3));

    private LoginService Service() => new(_accounts, Hasher, _throttle, new SessionIssuer(_tickets, _tokens), _moderation);

    [Fact]
    public async Task CorrectPasswordIssuesASsoTicketAndASeparateAccessToken()
    {
        var row = _accounts.Add("Dennis", Hasher.Hash("correct horse"));

        var result = await Service().Login("dennis", "correct horse", "10.0.0.1");

        Assert.Equal(LoginStatus.Success, result.Status);
        var session = result.Session!;
        Assert.Equal("Dennis", session.Username);
        Assert.Equal(row.Id, _tickets.Live[session.SsoTicket.Value]);
        Assert.Equal(row.Id, _tokens.Live[session.AccessToken.Value]);
        Assert.NotEqual(session.SsoTicket.Value, session.AccessToken.Value);
    }

    [Fact]
    public async Task WrongPasswordAndUnknownUserGetTheSameAnswerAndNoTokens()
    {
        _accounts.Add("Dennis", Hasher.Hash("correct horse"));

        var wrong = await Service().Login("Dennis", "wrong horse", "10.0.0.1");
        var unknown = await Service().Login("Nobody", "wrong horse", "10.0.0.1");

        Assert.Equal(new LoginResult(LoginStatus.InvalidCredentials), wrong);
        Assert.Equal(wrong, unknown);
        Assert.Empty(_tickets.Live);
        Assert.Empty(_tokens.Live);
    }

    [Fact]
    public async Task LegacyPlaintextPasswordIsUpgradedToArgon2idOnLogin()
    {
        var row = _accounts.Add("Dennis", "OctaneLocal-2026");

        var result = await Service().Login("Dennis", "OctaneLocal-2026", "10.0.0.1");

        Assert.Equal(LoginStatus.Success, result.Status);
        var stored = _accounts.Rows.Single(r => r.Id == row.Id).Password!;
        Assert.StartsWith("$argon2id$", stored);
        Assert.Equal(PasswordVerificationResult.Success, Hasher.Verify("OctaneLocal-2026", stored));
        Assert.Equal(LoginStatus.Success, (await Service().Login("Dennis", "OctaneLocal-2026", "10.0.0.1")).Status);
        Assert.Equal(LoginStatus.InvalidCredentials, (await Service().Login("Dennis", stored, "10.0.0.1")).Status);
    }

    [Fact]
    public async Task RepeatedFailuresLockTheAccountEvenForTheRightPassword()
    {
        _accounts.Add("Dennis", Hasher.Hash("correct horse"));
        var service = Service();
        for (var i = 0; i < 3; i++)
            await service.Login("Dennis", "guess" + i, "10.0.0." + i);

        var result = await service.Login("Dennis", "correct horse", "10.0.0.9");

        Assert.Equal(LoginStatus.Throttled, result.Status);
        Assert.InRange(result.RetryAfter, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
        Assert.Empty(_tickets.Live);
    }

    [Theory]
    [InlineData("Dennis")]
    [InlineData("10.0.0.1")]
    public async Task BannedAccountsOrAddressesGetNoSessionButOnlyAfterTheRightPassword(string banned)
    {
        _accounts.Add("Dennis", Hasher.Hash("correct horse"));
        _moderation.Bans[banned] = new ModerationBan(ModerationBanType.Username, banned, "Scamming", 2_000_000_000);

        var wrong = await Service().Login("Dennis", "wrong horse", "10.0.0.1");
        var right = await Service().Login("Dennis", "correct horse", "10.0.0.1");

        Assert.Equal(LoginStatus.InvalidCredentials, wrong.Status);
        Assert.Equal(LoginStatus.Banned, right.Status);
        Assert.Equal("Scamming", right.Ban!.Reason);
        Assert.Empty(_tickets.Live);
        Assert.Empty(_tokens.Live);
    }

    [Fact]
    public async Task LockingAnUnknownNameLooksTheSameAsLockingARealOne()
    {
        var service = Service();
        for (var i = 0; i < 3; i++)
            await service.Login("Ghost", "guess" + i, "10.0.0." + i);

        Assert.Equal(LoginStatus.Throttled, (await service.Login("Ghost", "guess", "10.0.0.9")).Status);
    }
}
