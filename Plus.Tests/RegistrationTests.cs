using Plus.Communication.Http;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Registration;
using Xunit;

namespace Plus.Tests;

public class RegistrationTests
{
    private static readonly Argon2idPasswordHasher Hasher = new();
    private readonly FakeAccounts _accounts = new();
    private readonly FakeSsoTickets _tickets = new();
    private readonly FakeAccessTokens _tokens = new();
    private readonly FakeRememberTokens _remember = new();
    private readonly FakeBans _bans = new();

    private RegistrationService Service(params string[] reserved) =>
        new(_accounts, new BoundedPasswordHasher(Hasher, AuthTestConfig.Options()), new SessionIssuer(_tickets, _tokens, _remember, _accounts, _bans), new FakeWordFilter("badword"), AuthTestConfig.Options(c => c.Registration.ReservedNames = reserved));

    private static RegistrationRequest Request(string username = "NewHabbo", string password = "long enough", string email = "new@example.com",
        string? figure = null, string? gender = null) => new(username, password, email, figure, gender, "10.0.0.1");

    [Theory]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnop")]
    [InlineData("has space")]
    [InlineData("ünicode")]
    [InlineData("<script>")]
    [InlineData("Moderator")]
    [InlineData("xAdmx")]
    [InlineData("m0dman")]
    [InlineData("mybadword")]
    [InlineData("TheOwner")]
    public void RejectsInvalidOrReservedNames(string username)
    {
        Assert.NotNull(RegistrationValidator.UsernameError(username, ["owner"], name => name.Contains("badword")));
    }

    [Theory]
    [InlineData("Dennis")]
    [InlineData("abc")]
    [InlineData("a.b,c_d-e;f:g?!")]
    [InlineData("Fifteen-Chars15")]
    public void AcceptsNamesFromTheInGameAlphabet(string username)
    {
        Assert.Null(RegistrationValidator.UsernameError(username, [], _ => false));
    }

    [Theory]
    [InlineData("player@localhost")]
    [InlineData("no-at-sign.example.com")]
    [InlineData("two@@example.com")]
    [InlineData("space @example.com")]
    [InlineData("<b>@example.com")]
    [InlineData("ünicode@example.com")]
    [InlineData("a@-example.com")]
    public void RejectsBadEmails(string email)
    {
        Assert.NotNull(RegistrationValidator.EmailError(email));
    }

    [Fact]
    public void AcceptsOrdinaryEmailsAndCapsLength()
    {
        Assert.Null(RegistrationValidator.EmailError("first.last+tag@mail.example.co.uk"));
        Assert.NotNull(RegistrationValidator.EmailError(new string('a', 64) + "@" + string.Join(".", Enumerable.Repeat(new string('b', 60), 4)) + ".com"));
    }

    [Fact]
    public void PasswordNeedsALengthRangeAndMustDifferFromTheName()
    {
        Assert.NotNull(RegistrationValidator.PasswordError("short", "Dennis"));
        Assert.NotNull(RegistrationValidator.PasswordError(new string('x', 129), "Dennis"));
        Assert.NotNull(RegistrationValidator.PasswordError("dennis12", "Dennis12"));
        Assert.Null(RegistrationValidator.PasswordError("dennis12!", "Dennis12"));
    }

    [Theory]
    [InlineData("hd-180-1.hr-100-61.ch-210-66.lg-270-82.sh-290-80", true)]
    [InlineData("ch-3334-93-1408.hd-180-7", true)]
    [InlineData("hd", false)]
    [InlineData("hd-180-1.", false)]
    [InlineData("hd-180-1;DROP", false)]
    [InlineData("", false)]
    public void KeepsOnlyWellFormedFigures(string figure, bool kept)
    {
        Assert.Equal(kept ? figure : "fallback", RegistrationValidator.FigureOrDefault(figure, "fallback"));
    }

    [Fact]
    public async Task RegistersWithAHashedPasswordAndConfiguredLook()
    {
        var result = await Service().Register(Request(figure: "bogus", gender: "f"));

        Assert.Equal(RegistrationStatus.Created, result.Status);
        var created = Assert.Single(_accounts.Created);
        var userId = _accounts.Rows.Single(r => r.Username == "NewHabbo").Id;
        Assert.Equal("NewHabbo", result.Session!.Username);
        Assert.Equal(userId, _tickets.Live[result.Session.SsoTicket.Value]);
        Assert.Equal(userId, _tokens.Live[result.Session.AccessToken.Value]);
        Assert.Equal(PasswordVerificationResult.Success, Hasher.Verify("long enough", created.PasswordHash));
        Assert.Equal(new RegistrationDefaults().Look, created.Look);
        Assert.Equal("F", created.Gender);
        Assert.Equal("10.0.0.1", created.Address);
    }

    [Fact]
    public async Task RefusesTakenNamesAndEmailsCaseInsensitively()
    {
        _accounts.Add("Dennis", "x");
        _accounts.Emails.Add("taken@example.com");

        Assert.Equal(RegistrationStatus.UsernameTaken, (await Service().Register(Request(username: "dennis"))).Status);
        Assert.Equal(RegistrationStatus.EmailTaken, (await Service().Register(Request(email: "TAKEN@example.com"))).Status);
        Assert.Empty(_tickets.Live);
        Assert.Empty(_accounts.Created);
    }

    [Fact]
    public async Task ConcurrentRegistrationsWithOneEmailCreateOneAccount()
    {
        var service = Service();
        _accounts.ConcurrentEmailChecks = 8;

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => service.Register(Request(username: "Racer" + i, email: "same@example.com")))));

        Assert.Single(results, r => r.Status == RegistrationStatus.Created);
        Assert.Single(_accounts.Created);
    }

    [Fact]
    public async Task AvailabilityChecksReportValidationAndTakenNames()
    {
        _accounts.Add("Dennis", "x");
        _accounts.Emails.Add("taken@example.com");
        var service = Service("owner");

        Assert.Equal(new Availability(true), await service.CheckUsername("Fresh"));
        Assert.False((await service.CheckUsername("DENNIS")).Available);
        Assert.Equal(new Availability(false, RegistrationStatus.UsernameTaken, "That Habbo name is already taken."), await service.CheckUsername("DENNIS"));
        Assert.Equal(RegistrationStatus.Invalid, (await service.CheckUsername("TheOwner")).Reason);
        Assert.Equal(RegistrationStatus.EmailTaken, (await service.CheckEmail("taken@example.com")).Reason);
        Assert.NotEmpty((await service.CheckUsername("x")).Error);
        Assert.Equal(new Availability(true), await service.CheckEmail("fresh@example.com"));
        Assert.False((await service.CheckEmail("taken@example.com")).Available);
        Assert.False((await service.CheckEmail("not-an-email")).Available);
    }

    [Fact]
    public async Task InvalidInputIsRejectedBeforeTouchingTheDatabase()
    {
        var result = await Service().Register(Request(password: "short"));

        Assert.Equal(RegistrationStatus.Invalid, result.Status);
        Assert.NotEmpty(result.Error);
        Assert.Empty(_accounts.Created);
    }
}
