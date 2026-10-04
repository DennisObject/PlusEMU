using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog.Extensions.Logging;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Registration;

namespace Plus.Communication.Http;

public interface IAuthHttpServer
{
    Task Start();
    Task Stop();

    /// <summary>Bound addresses once started, e.g. http://127.0.0.1:8080.</summary>
    IReadOnlyCollection<string> Urls { get; }
}

/// <summary>
/// Kestrel host for the hotel's login API (/api/auth/*, /api/health, /api/maintenance).
/// TLS is terminated by the reverse proxy in front of it.
/// </summary>
public class AuthHttpServer : IAuthHttpServer
{
    public const long MaxRequestBodyBytes = 16 * 1024;

    private readonly AuthApiConfiguration _configuration;
    private readonly AuthEndpoints _endpoints;
    private WebApplication? _app;

    public AuthHttpServer(IOptions<AuthApiConfiguration> options, ILoginService login, IRegistrationService registration, ISsoTicketStore ssoTickets,
        IAccessTokenStore accessTokens)
    {
        _configuration = options.Value;
        _endpoints = new(login, registration, ssoTickets, accessTokens);
    }

    public IReadOnlyCollection<string> Urls => _app?.Urls.ToList() ?? [];

    internal IServiceProvider? Services => _app?.Services;

    public async Task Start()
    {
        // The empty builder reads no appsettings or ASPNETCORE_* variables and watches no files:
        // config.json is the only source. Production keeps stack traces out of error responses.
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = [], EnvironmentName = Environments.Production });
        builder.Logging.AddNLog(new NLogProviderOptions { RemoveLoggerFactoryFilter = false }).AddFilter("Microsoft", LogLevel.Warning);
        builder.WebHost.UseKestrelCore().ConfigureKestrel(ConfigureKestrel);
        builder.Services.AddRoutingCore();
        builder.Services.AddSingleton<IHostLifetime, EmulatorOwnedLifetime>();
        builder.Services.Configure<ForwardedHeadersOptions>(ConfigureForwardedHeaders);
        builder.Services.AddRateLimiter(ConfigureRateLimiter);

        var app = builder.Build();
        // With no known proxies at all the middleware would trust every sender, so it only runs
        // when proxies are configured.
        if (_configuration.TrustedProxies.Length > 0)
            app.UseForwardedHeaders();
        app.UseExceptionHandler(error => error.Run(context => WriteError(context, StatusCodes.Status500InternalServerError)));
        app.UseStatusCodePages(context => WriteError(context.HttpContext, context.HttpContext.Response.StatusCode));
        app.Use(AddSecurityHeaders);
        app.UseRateLimiter();
        _endpoints.Map(app);

        await app.StartAsync();
        _app = app;
    }

    public async Task Stop()
    {
        if (_app == null)
            return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }

    /// <summary>The caller's address after X-Forwarded-For from a trusted proxy is applied.</summary>
    public static string ClientAddress(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address == null)
            return "unknown";
        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    private void ConfigureKestrel(Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions kestrel)
    {
        kestrel.AddServerHeader = false;
        kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        kestrel.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
        kestrel.Limits.MaxRequestLineSize = 4 * 1024;
        kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
        // Trickled bodies are dropped after 5 seconds below 240 bytes/s.
        kestrel.Limits.MinRequestBodyDataRate = new(240, TimeSpan.FromSeconds(5));
        kestrel.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);

        if (string.Equals(_configuration.Hostname, "localhost", StringComparison.OrdinalIgnoreCase))
            kestrel.ListenLocalhost(_configuration.Port);
        else
            kestrel.Listen(IPAddress.Parse(_configuration.Hostname), _configuration.Port);
    }

    private void ConfigureForwardedHeaders(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
        options.ForwardLimit = 1;
        // Only the configured proxies are trusted, not the framework's loopback default.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in _configuration.TrustedProxies)
        {
            if (proxy.Contains('/'))
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(proxy));
            else
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        }
    }

    private void ConfigureRateLimiter(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, _) =>
        {
            SetRetryAfter(context.HttpContext.Response, context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : TimeSpan.FromMinutes(1));
            return new(WriteError(context.HttpContext, StatusCodes.Status429TooManyRequests));
        };
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => context.Request.Path.StartsWithSegments("/api/auth")
            ? RateLimitPartition.GetFixedWindowLimiter(ClientAddress(context), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = _configuration.RequestsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            })
            : RateLimitPartition.GetNoLimiter(""));
    }

    private static Task AddSecurityHeaders(HttpContext context, Func<Task> next)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return next();
    }

    /// <summary>Retry-After in whole seconds, never less than one.</summary>
    public static void SetRetryAfter(HttpResponse response, TimeSpan wait) =>
        response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static (string Code, string Error) ErrorFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => (AuthErrorCode.NotFound, "Not found."),
        StatusCodes.Status405MethodNotAllowed => (AuthErrorCode.MethodNotAllowed, "Method not allowed."),
        StatusCodes.Status413PayloadTooLarge => (AuthErrorCode.PayloadTooLarge, "Request too large."),
        StatusCodes.Status429TooManyRequests => (AuthErrorCode.RateLimited, AuthEndpoints.TooManyAttempts),
        >= 500 => (AuthErrorCode.ServerError, "Something went wrong. Please try again."),
        _ => (AuthErrorCode.InvalidRequest, "Invalid request.")
    };

    private static Task WriteError(HttpContext context, int status)
    {
        var (code, error) = ErrorFor(status);
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(new { error, code });
    }

    /// <summary>
    /// The emulator owns process shutdown (Program handles SIGTERM/SIGINT and calls Stop). The
    /// default ConsoleLifetime would cancel the signal and wait for a host Run() that never comes.
    /// </summary>
    private sealed class EmulatorOwnedLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
