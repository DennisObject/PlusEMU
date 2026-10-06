using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Plus.Communication.Http;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Registration;
using Xunit;

namespace Plus.Tests;

public sealed class AuthHttpServerTests : IAsyncLifetime
{
    private static readonly Argon2idPasswordHasher Hasher = new();

    private readonly FakeAccounts _accounts = new();
    private readonly FakeSsoTickets _tickets = new();
    private readonly FakeAccessTokens _tokens = new();
    private readonly FakeBans _bans = new();
    private readonly FakeRememberTokens _remember = new();
    private SessionIssuer? _sessions;
    private IPasswordHasher _innerHasher = Hasher;
    private CountingHasher _hasher
    {
        get => (CountingHasher)_innerHasher;
        set => _innerHasher = value;
    }
    private HttpClient _http = new();
    private AuthHttpServer? _server;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _http.Dispose();

        if (_server != null) {
            await _server.Stop();
        }
    }

    private async Task Start(Action<AuthApiConfiguration>? configure = null)
    {
        var options = AuthTestConfig.Options(c =>
        {
            c.Port = 0;
            c.MaxFailedLoginsPerAccount = 3;
            configure?.Invoke(c);
        });
        var sessions = _sessions = new SessionIssuer(_tickets, _tokens, _remember, new FakeGenerations(), _accounts, _bans, TimeProvider.System);
        var hasher = new BoundedPasswordHasher(_innerHasher, options);
        var login = new LoginService(_accounts, hasher, new LoginThrottle(TimeProvider.System, options), sessions, _bans);
        var registration = new RegistrationService(_accounts, hasher, sessions, new FakeWordFilter(), options);
        _server = new AuthHttpServer(options, login, registration, sessions);
        await _server.Start();
        _http.Dispose();
        _http = new HttpClient { BaseAddress = new Uri(_server.Urls.Single()) };
    }

    private Task<HttpResponseMessage> Post(string path, object body, string? forwardedFor = null, string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };

        if (forwardedFor != null) {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        if (bearer != null) {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return _http.SendAsync(request);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task ServesHealthMaintenanceAndRoomTemplatesWithSafeHeaders()
    {
        await Start();

        var health = await _http.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.True((await Json(health)).GetProperty("ok").GetBoolean());
        Assert.False((await Json(await _http.GetAsync("/api/maintenance"))).GetProperty("enabled").GetBoolean());
        Assert.Equal(0, (await Json(await _http.GetAsync("/api/auth/room-templates"))).GetProperty("templates").GetArrayLength());

        Assert.True(health.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", health.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.False(health.Headers.Contains("Server"));
    }

    [Fact]
    public async Task DisabledAuthKeepsHealthAvailableAndRefusesEveryAuthRoute()
    {
        await Start(configuration => configuration.Enabled = false);

        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/api/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/api/maintenance")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post("/api/auth/login", new { username = "Dennis", password = "secret" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post("/api/auth/sso-token", new { ssoTicket = "external-ticket" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/auth/room-templates")).StatusCode);
    }

    [Fact]
    public async Task TheHostLeavesStopSignalsToTheEmulatorAndStopsWhenAsked()
    {
        await Start();

        // ASP.NET's default ConsoleLifetime swallows SIGTERM and waits for a host Run() that the
        // emulator never calls, which kept the process alive.
        var lifetime = _server!.Services!.GetRequiredService<Microsoft.Extensions.Hosting.IHostLifetime>();
        Assert.DoesNotContain("Console", lifetime.GetType().Name);

        var url = _server.Urls.Single();
        await _server.Stop();
        _server = null;
        await Assert.ThrowsAsync<HttpRequestException>(() => new HttpClient().GetAsync(url + "/api/health"));
    }

    [Fact]
    public async Task UnknownRoutesAre404NotAnEmptySuccess()
    {
        await Start();

        var response = await Post("/api/auth/change-password", new { password = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Not found.", (await Json(response)).GetProperty("error").GetString());
        Assert.Equal(AuthErrorCode.NotFound, (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task LoginReturnsTheTicketAndAccessTokenTheClientStores()
    {
        var row = _accounts.Add("Dennis", Hasher.Hash("correct horse"));
        await Start();

        var response = await Post("/api/auth/login", new { username = " dennis ", password = "correct horse", remember = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("Dennis", body.GetProperty("username").GetString());
        Assert.Equal(row.Id, _tickets.Live[body.GetProperty("ssoTicket").GetString()!]);
        Assert.Equal(row.Id, _tokens.Live[body.GetProperty("accessToken").GetString()!]);
        Assert.Equal(2000, body.GetProperty("accessTokenExpiresAt").GetInt64());
    }

    [Fact]
    public async Task FailedLoginsGetOneGenericAnswerAndThenAreThrottled()
    {
        _accounts.Add("Dennis", Hasher.Hash("correct horse"));
        await Start();

        var wrong = await Post("/api/auth/login", new { username = "Dennis", password = "nope" });
        var unknown = await Post("/api/auth/login", new { username = "Nobody", password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(AuthErrorCode.InvalidCredentials, (await Json(wrong)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await Post("/api/auth/login", new { username = "Dennis" })).StatusCode);

        await Post("/api/auth/login", new { username = "Dennis", password = "nope" });
        await Post("/api/auth/login", new { username = "Dennis", password = "nope" });
        var locked = await Post("/api/auth/login", new { username = "Dennis", password = "correct horse" });

        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal(AuthEndpoints.TooManyAttempts, (await Json(locked)).GetProperty("error").GetString());
        Assert.Equal(AuthErrorCode.RateLimited, (await Json(locked)).GetProperty("code").GetString());
        Assert.InRange(int.Parse(locked.Headers.GetValues("Retry-After").Single()), 890, 900);
        Assert.Empty(_tickets.Live);
    }

    [Fact]
    public async Task RegistrationReportsCreatedTakenAndInvalid()
    {
        _accounts.Add("Taken", "x");
        await Start();

        var created = await Post("/api/auth/register", new { username = "NewHabbo", email = "new@example.com", password = "long enough", figure = "hd-180-1", gender = "M", templateId = 3 });
        var taken = await Post("/api/auth/register", new { username = "taken", email = "other@example.com", password = "long enough" });
        var emailTaken = await Post("/api/auth/register", new { username = "Another", email = "NEW@example.com", password = "long enough" });
        var invalid = await Post("/api/auth/register", new { username = "Mod", email = "x@example.com", password = "long enough" });

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var session = await Json(created);
        Assert.Equal("NewHabbo", session.GetProperty("username").GetString());
        var userId = _accounts.Rows.Single(r => r.Username == "NewHabbo").Id;
        Assert.Equal(userId, _tickets.Live[session.GetProperty("ssoTicket").GetString()!]);
        Assert.Equal(userId, _tokens.Live[session.GetProperty("accessToken").GetString()!]);
        Assert.Equal(2000, session.GetProperty("accessTokenExpiresAt").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.False((await Json(taken)).GetProperty("available").GetBoolean());
        Assert.Equal(AuthErrorCode.NameTaken, (await Json(taken)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, emailTaken.StatusCode);
        Assert.Equal(AuthErrorCode.EmailTaken, (await Json(emailTaken)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("That Habbo name is not allowed.", (await Json(invalid)).GetProperty("error").GetString());
        Assert.Equal(AuthErrorCode.Validation, (await Json(invalid)).GetProperty("code").GetString());
        Assert.StartsWith("$argon2id$", Assert.Single(_accounts.Created).PasswordHash);
    }

    [Fact]
    public async Task AvailabilityChecksAnswerHonestly()
    {
        _accounts.Add("Taken", "x");
        _accounts.Emails.Add("taken@example.com");
        await Start();

        Assert.True((await Json(await Post("/api/auth/check-username", new { username = "Fresh" }))).GetProperty("available").GetBoolean());
        var takenName = await Json(await Post("/api/auth/check-username", new { username = "TAKEN" }));
        Assert.False(takenName.GetProperty("available").GetBoolean());
        Assert.Equal("That Habbo name is already taken.", takenName.GetProperty("error").GetString());
        Assert.Equal(AuthErrorCode.NameTaken, takenName.GetProperty("code").GetString());
        Assert.Equal(AuthErrorCode.Validation, (await Json(await Post("/api/auth/check-username", new { username = "x" }))).GetProperty("code").GetString());
        Assert.Equal(AuthErrorCode.EmailTaken, (await Json(await Post("/api/auth/check-email", new { email = "taken@example.com" }))).GetProperty("code").GetString());
        Assert.False((await Json(await Post("/api/auth/check-email", new { email = "taken@example.com" }))).GetProperty("available").GetBoolean());
        Assert.True((await Json(await Post("/api/auth/check-email", new { email = "fresh@example.com" }))).GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task ForgotPasswordGivesTheSameHonestAnswerForAnyAddress()
    {
        _accounts.Emails.Add("taken@example.com");
        await Start();

        var known = await Post("/api/auth/forgot-password", new { email = "taken@example.com" });
        var unknown = await Post("/api/auth/forgot-password", new { email = "nobody@example.com" });

        Assert.Equal(HttpStatusCode.NotImplemented, known.StatusCode);
        Assert.Equal(AuthErrorCode.NotImplemented, (await Json(known)).GetProperty("code").GetString());
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SsoTicketExchangeIssuesAnAccessTokenWithoutSpendingTheTicket()
    {
        await Start();
        var ticket = await _tickets.Issue(42);

        var response = await Post("/api/auth/sso-token", new { ssoTicket = ticket.Value });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await Json(response)).GetProperty("accessToken").GetString()!;
        Assert.NotEqual(ticket.Value, token);
        Assert.Equal(42, _tokens.Live[token]);
        var again = await Post("/api/auth/sso-token", new { ssoTicket = ticket.Value });
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        Assert.Equal(AuthErrorCode.InvalidTicket, (await Json(again)).GetProperty("code").GetString());
        Assert.Single(_tokens.Live);
        Assert.Equal(42, await _tickets.Consume(ticket.Value));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/auth/sso-token", new { ssoTicket = ticket.Value })).StatusCode);
        var empty = await Post("/api/auth/sso-token", new { ssoTicket = "" });
        Assert.Equal(HttpStatusCode.Unauthorized, empty.StatusCode);
        Assert.Equal(AuthErrorCode.InvalidTicket, (await Json(empty)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task LogoutRevokesTheBearerAccessToken()
    {
        await Start();
        var token = await _tokens.Issue(7);

        var response = await Post("/api/auth/logout", new { ssoTicket = "", rememberToken = "" }, bearer: token.Value);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await Json(response)).GetProperty("ok").GetBoolean());
        Assert.Null(await _tokens.FindUser(token.Value));
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/login/")]
    [InlineData("/API/AUTH/LOGIN/")]
    public async Task PasswordHashingRunsAtMostTheConfiguredNumberAtOnceWhateverTheRoute(string loginPath)
    {
        _hasher = new CountingHasher(Hasher, TimeSpan.FromMilliseconds(150));
        await Start(c => c.MaxConcurrentPasswordChecks = 1);

        var logins = Enumerable.Range(0, 4).Select(i => Post(loginPath, new { username = "Nobody" + i, password = "x" }));
        var registers = Enumerable.Range(0, 2).Select(i => Post("/api/auth/register/", new { username = "Racer" + i, email = $"r{i}@example.com", password = "long enough" }));
        var responses = await Task.WhenAll(logins.Concat(registers));

        Assert.All(responses.Take(4), r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.All(responses.Skip(4), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, _hasher.MaxConcurrent);
    }

    [Fact]
    public async Task LoginsBeyondTheHashingQueueGetRateLimited()
    {
        var held = new HeldHasher();
        _innerHasher = held;
        await Start(c =>
        {
            c.MaxConcurrentPasswordChecks = 1;
            c.MaxQueuedPasswordChecks = 0;
        });

        var first = Post("/api/auth/login", new { username = "Nobody", password = "x" });
        await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var refused = await Post("/api/auth/login", new { username = "Other", password = "x" }).WaitAsync(TimeSpan.FromSeconds(5));
        held.Release.Set();

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(AuthErrorCode.RateLimited, (await Json(refused)).GetProperty("code").GetString());
        Assert.Equal("5", refused.Headers.GetValues("Retry-After").Single());
        Assert.Equal(HttpStatusCode.Unauthorized, (await first).StatusCode);
    }

    [Fact]
    public async Task ASlowRequestBodyDoesNotHoldAPasswordHashingSlot()
    {
        await Start(c => c.MaxConcurrentPasswordChecks = 1);
        var uri = new Uri(_server!.Urls.Single());
        using var slow = new System.Net.Sockets.TcpClient();
        await slow.ConnectAsync(uri.Host, uri.Port);
        var stream = slow.GetStream();
        var partial = Encoding.ASCII.GetBytes("POST /api/auth/login HTTP/1.1\r\nHost: x\r\nContent-Type: application/json\r\nContent-Length: 60\r\n\r\n{\"username\":\"Slow");
        await stream.WriteAsync(partial);
        await Task.Delay(200);

        var normal = await Post("/api/auth/login", new { username = "Nobody", password = "x" }).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(HttpStatusCode.Unauthorized, normal.StatusCode);
    }

    [Fact]
    public async Task UnexpectedFailuresAnswerWithAGenericJsonError()
    {
        _accounts.FailLookupsWith = new InvalidOperationException("Server=db;Password=secret");
        await Start();

        var response = await Post("/api/auth/login", new { username = "Dennis", password = "x" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Something went wrong. Please try again.", JsonDocument.Parse(body).RootElement.GetProperty("error").GetString());
        Assert.Equal(AuthErrorCode.ServerError, JsonDocument.Parse(body).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("secret", body);
    }

    [Fact]
    public async Task LogoutWithTheBearerAlsoClearsTheUsersOutstandingTicket()
    {
        _accounts.Add("Dennis", Hasher.Hash("correct horse"));
        await Start();
        var session = await Json(await Post("/api/auth/login", new { username = "Dennis", password = "correct horse" }));
        var ticket = session.GetProperty("ssoTicket").GetString()!;

        await Post("/api/auth/logout", new { }, bearer: session.GetProperty("accessToken").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/auth/sso-token", new { ssoTicket = ticket })).StatusCode);
        Assert.Null(await _tickets.Consume(ticket));
        Assert.Empty(_tokens.Live);
    }

    [Fact]
    public async Task LogoutClearsATicketSentInTheBody()
    {
        await Start();
        var ticket = await _tickets.Issue(42);

        var response = await Post("/api/auth/logout", new { ssoTicket = ticket.Value, rememberToken = "" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await _tickets.Consume(ticket.Value));
    }

    [Fact]
    public async Task RegistrationRevokedWhileItRanAsksTheUserToLogIn()
    {
        _accounts.RevokeOnCreate = userId => _sessions!.RevokeAll(userId);
        await Start();

        var response = await Post("/api/auth/register", new { username = "NewHabbo", email = "new@example.com", password = "long enough" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await Json(response);
        Assert.Equal(AuthErrorCode.InvalidCredentials, body.GetProperty("code").GetString());
        Assert.Equal("Your account was created. Please log in.", body.GetProperty("error").GetString());
        Assert.Empty(_tickets.Live);
    }

    [Fact]
    public async Task OnlyARememberedLoginReturnsARememberToken()
    {
        _accounts.Add("Dennis", Hasher.Hash("correct horse"));
        await Start();

        var remembered = await Json(await Post("/api/auth/login", new { username = "Dennis", password = "correct horse", remember = true }));
        var plain = await Json(await Post("/api/auth/login", new { username = "Dennis", password = "correct horse", remember = false }));

        Assert.True(_remember.IsLive(remembered.GetProperty("rememberToken").GetString()!));
        Assert.Equal(3000, remembered.GetProperty("rememberExpiresAt").GetInt64());
        Assert.False(plain.TryGetProperty("rememberToken", out _));
        Assert.False(plain.TryGetProperty("rememberExpiresAt", out _));
    }

    [Fact]
    public async Task RememberLogsInWithARotatedTokenAndAReplayEndsTheFamily()
    {
        var row = _accounts.Add("Dennis", Hasher.Hash("correct horse"));
        await Start();
        var first = (await Json(await Post("/api/auth/login", new { username = "Dennis", password = "correct horse", remember = true }))).GetProperty("rememberToken").GetString()!;

        var resumed = await Post("/api/auth/remember", new { rememberToken = first });

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        var session = await Json(resumed);
        Assert.Equal("Dennis", session.GetProperty("username").GetString());
        Assert.Equal(row.Id, session.GetProperty("userId").GetInt32());
        Assert.Equal(row.Id, _tickets.Live[session.GetProperty("ssoTicket").GetString()!]);
        Assert.Equal(row.Id, _tokens.Live[session.GetProperty("accessToken").GetString()!]);
        var second = session.GetProperty("rememberToken").GetString()!;
        Assert.NotEqual(first, second);
        Assert.Equal(3000, session.GetProperty("rememberExpiresAt").GetInt64());

        var replay = await Post("/api/auth/remember", new { rememberToken = first });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal(AuthErrorCode.InvalidRememberToken, (await Json(replay)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/auth/remember", new { rememberToken = second })).StatusCode);
    }

    [Fact]
    public async Task RefreshRotatesTheRememberTokenAndIssuesOnlyAnAccessToken()
    {
        var row = _accounts.Add("Dennis", "x");
        await Start();
        var token = await _remember.Issue(row.Id);

        var refreshed = await Post("/api/auth/refresh", new { rememberToken = token.Value });

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var body = await Json(refreshed);
        Assert.False(body.TryGetProperty("ssoTicket", out _));
        Assert.Equal("Dennis", body.GetProperty("username").GetString());
        Assert.Equal(row.Id, body.GetProperty("userId").GetInt32());
        Assert.Equal(row.Id, _tokens.Live[body.GetProperty("accessToken").GetString()!]);
        Assert.True(_remember.IsLive(body.GetProperty("rememberToken").GetString()!));
        Assert.False(_remember.IsLive(token.Value));
        Assert.Empty(_tickets.Live);
    }

    [Fact]
    public async Task RememberAndRefreshRejectUnknownOrMissingTokens()
    {
        await Start();

        var unknown = await Post("/api/auth/refresh", new { rememberToken = SecureToken.Generate() });
        var missing = await Post("/api/auth/remember", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(AuthErrorCode.InvalidRememberToken, (await Json(unknown)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task ABannedUsersRememberTokenIsRefusedAndRevoked()
    {
        var row = _accounts.Add("Dennis", "x");
        _bans.ByUsernameOrAddress["Dennis"] = new LoginBan("Scamming", DateTimeOffset.FromUnixTimeSeconds(2_000_000_000));
        await Start();
        var token = await _remember.Issue(row.Id);
        var otherDevice = await _remember.Issue(row.Id);

        var response = await Post("/api/auth/remember", new { rememberToken = token.Value });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(AuthErrorCode.Banned, (await Json(response)).GetProperty("code").GetString());
        Assert.False(_remember.IsLive(otherDevice.Value));
        Assert.Empty(_tickets.Live);
        Assert.Empty(_tokens.Live);
    }

    [Fact]
    public async Task LogoutRevokesTheRememberTokenFromTheBody()
    {
        var row = _accounts.Add("Dennis", "x");
        await Start();
        var token = await _remember.Issue(row.Id);

        await Post("/api/auth/logout", new { ssoTicket = "", rememberToken = token.Value });

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/auth/remember", new { rememberToken = token.Value })).StatusCode);
    }

    [Fact]
    public async Task RejectsOversizedAndMalformedBodiesWithJsonErrors()
    {
        await Start();

        var oversized = await Post("/api/auth/login", new { username = "Dennis", password = new string('x', (int)AuthHttpServer.MaxRequestBodyBytes) });
        var malformed = await _http.PostAsync("/api/auth/login", new StringContent("{\"username\":", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("Invalid request.", (await Json(malformed)).GetProperty("error").GetString());
        Assert.Equal(AuthErrorCode.InvalidRequest, (await Json(malformed)).GetProperty("code").GetString());
        Assert.Equal(AuthErrorCode.PayloadTooLarge, (await Json(oversized)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task RateLimitsEachClientAddressAcrossAuthRoutes()
    {
        await Start(c => c.RequestsPerMinute = 3);

        for (var i = 0; i < 3; i++) {
            Assert.Equal(HttpStatusCode.OK, (await Post("/api/auth/check-username", new { username = "Fresh" + i })).StatusCode);
        }

        var limited = await Post("/api/auth/check-email", new { email = "a@example.com" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(AuthEndpoints.TooManyAttempts, (await Json(limited)).GetProperty("error").GetString());
        Assert.Equal(AuthErrorCode.RateLimited, (await Json(limited)).GetProperty("code").GetString());
        Assert.InRange(int.Parse(limited.Headers.GetValues("Retry-After").Single()), 1, 60);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task BannedAccountsAreToldWhyOnceThePasswordIsRight()
    {
        _accounts.Add("Dennis", Hasher.Hash("correct horse"));
        _bans.ByUsernameOrAddress["Dennis"] = new LoginBan("Scamming", DateTimeOffset.FromUnixTimeSeconds(2_000_000_000));
        await Start();

        var wrong = await Post("/api/auth/login", new { username = "Dennis", password = "nope" });
        var banned = await Post("/api/auth/login", new { username = "Dennis", password = "correct horse" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, banned.StatusCode);
        var body = await Json(banned);
        Assert.Equal(AuthErrorCode.Banned, body.GetProperty("code").GetString());
        Assert.Equal("Scamming", body.GetProperty("banReason").GetString());
        Assert.Equal(2_000_000_000, body.GetProperty("banExpiresAt").GetInt64());
        Assert.False(body.TryGetProperty("ssoTicket", out _));
        Assert.Empty(_tickets.Live);
    }

    [Theory]
    [InlineData("127.0.0.0/8", "203.0.113.7")]
    [InlineData("127.0.0.1", "203.0.113.7")]
    [InlineData("10.9.9.9", "127.0.0.1")]
    [InlineData("", "127.0.0.1")]
    public async Task ForwardedForIsHonouredOnlyFromConfiguredProxies(string trusted, string expectedAddress)
    {
        await Start(c => c.TrustedProxies = trusted.Length == 0 ? [] : [trusted]);

        await Post("/api/auth/register", new { username = "ViaProxy", email = "proxy@example.com", password = "long enough" }, forwardedFor: "203.0.113.7");

        Assert.Equal(expectedAddress, Assert.Single(_accounts.Created).Address);
        Assert.Equal(expectedAddress, _accounts.LastAddress[_accounts.Rows.Single(r => r.Username == "ViaProxy").Id]);
    }
}
